using Carpoolr.Api.Data;
using Carpoolr.Api.Features.Profiles;
using Carpoolr.Api.Features.Trips;
using Carpoolr.Api.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Carpoolr.Api.Features.Bookings;

public sealed record BookingDetails(TripBooking Booking, Trip Trip);
public sealed record BookingPage(IReadOnlyList<BookingDetails> Entries, int? NextOffset);

public sealed class BookingApplicationService(
    CarpoolrDbContext database, TripApplicationService trips, ProfileApplicationService profiles, TimeProvider clock)
{
    public async Task<BookingDetails> RequestAsync(
        Guid tripId, Guid requestId, UserIdentity actor, CancellationToken cancellationToken)
    {
        var profile = await profiles.RequireAsync(actor, cancellationToken);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var trip = await trips.LockAsync(tripId, cancellationToken);
        var previous = await database.Bookings.AsNoTracking().SingleOrDefaultAsync(
            booking => booking.ProfileId == profile.Id && booking.RequestId == requestId, cancellationToken);
        if (previous is not null)
        {
            if (previous.TripId != tripId) throw new TripConflictException();
            await transaction.CommitAsync(cancellationToken);
            return await ReadAsync(previous.Id, cancellationToken);
        }
        EnsureOpen(trip);
        if (ApplicationRules.IsOwner(trip, actor))
            throw new TripPreconditionException("Drivers cannot request a passenger seat on their own trip.");
        if (await database.Bookings.AnyAsync(booking => booking.TripId == tripId && booking.ProfileId == profile.Id &&
            (booking.State == BookingState.Pending || booking.State == BookingState.Accepted), cancellationToken))
            throw new TripPreconditionException("You already have an active request for this trip.");
        if (await database.Bookings.CountAsync(booking => booking.TripId == tripId && booking.State == BookingState.Accepted,
            cancellationToken) >= trip.SeatsTotal)
            throw new TripPreconditionException("The trip has no available passenger seats.");
        if (await database.Bookings.CountAsync(booking => booking.TripId == tripId && booking.State == BookingState.Pending,
            cancellationToken) >= 100)
            throw new TripPreconditionException("This trip has reached its pending-request limit.");

        var booking = new TripBooking
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            TripId = tripId,
            ProfileId = profile.Id,
            State = BookingState.Pending,
            RequestedAt = clock.GetUtcNow(),
        };
        database.Bookings.Add(booking);
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new TripConflictException(); }
        await transaction.CommitAsync(cancellationToken);
        return await ReadAsync(booking.Id, cancellationToken);
    }

    public Task<BookingDetails> AcceptAsync(Guid id, UserIdentity actor, CancellationToken cancellationToken) =>
        ChangeAsync(id, BookingState.Accepted, actor, cancellationToken);
    public Task<BookingDetails> RejectAsync(Guid id, UserIdentity actor, CancellationToken cancellationToken) =>
        ChangeAsync(id, BookingState.Rejected, actor, cancellationToken);
    public Task<BookingDetails> WithdrawAsync(Guid id, UserIdentity actor, CancellationToken cancellationToken) =>
        ChangeAsync(id, BookingState.Withdrawn, actor, cancellationToken);

    private async Task<BookingDetails> ChangeAsync(
        Guid id, BookingState target, UserIdentity actor, CancellationToken cancellationToken)
    {
        var identifier = await database.Bookings.AsNoTracking().Where(booking => booking.Id == id)
            .Select(booking => new { booking.TripId }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new TripNotFoundException();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var trip = await trips.LockAsync(identifier.TripId, cancellationToken);
        var booking = await database.Bookings.Include(value => value.Profile).SingleAsync(value => value.Id == id, cancellationToken);
        var requester = booking.Profile.Issuer == actor.Issuer && booking.Profile.Subject == actor.Subject;
        if (target == BookingState.Withdrawn ? !requester : !ApplicationRules.IsOwner(trip, actor))
            throw new TripPermissionException();
        if (booking.State == target)
        {
            await transaction.CommitAsync(cancellationToken);
            return await ReadAsync(id, cancellationToken);
        }
        EnsureOpen(trip);
        if (target == BookingState.Withdrawn)
        {
            if (booking.State is not (BookingState.Pending or BookingState.Accepted))
                throw new TripPreconditionException("Only pending or accepted requests can be withdrawn.");
        }
        else if (booking.State != BookingState.Pending)
            throw new TripPreconditionException("Only pending requests can be approved or rejected.");
        if (target == BookingState.Accepted && await database.Bookings.CountAsync(
                value => value.TripId == trip.Id && value.State == BookingState.Accepted, cancellationToken) >= trip.SeatsTotal)
            throw new TripPreconditionException("The trip has no available passenger seats.");
        booking.State = target;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ReadAsync(id, cancellationToken);
    }

    public async Task<BookingPage> ListMineAsync(UserIdentity actor, int offset, int pageSize, CancellationToken cancellationToken)
    {
        offset = ApplicationRules.Offset(offset);
        pageSize = ApplicationRules.PageSize(pageSize);
        var bookings = await database.Bookings.AsNoTracking().Include(booking => booking.Profile)
            .Where(booking => booking.Profile.Issuer == actor.Issuer && booking.Profile.Subject == actor.Subject)
            .OrderByDescending(booking => booking.RequestedAt).ThenBy(booking => booking.Id)
            .Skip(offset).Take(pageSize + 1).ToListAsync(cancellationToken);
        var more = bookings.Count > pageSize;
        if (more) bookings.RemoveAt(bookings.Count - 1);
        var tripDetails = await trips.GetManyAsync(bookings.Select(booking => booking.TripId), cancellationToken);
        var entries = new List<BookingDetails>();
        foreach (var booking in bookings)
            entries.Add(new BookingDetails(booking, tripDetails[booking.TripId]));
        return new BookingPage(entries, more && offset + pageSize <= 10000 ? offset + pageSize : null);
    }

    private async Task<BookingDetails> ReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var booking = await database.Bookings.AsNoTracking().Include(value => value.Profile)
            .SingleAsync(value => value.Id == id, cancellationToken);
        var trip = await trips.GetAsync(booking.TripId, cancellationToken);
        return new BookingDetails(booking, trip);
    }

    private void EnsureOpen(Trip trip)
    {
        if (trip.State != TripState.Planned || trip.DepartureAt <= clock.GetUtcNow())
            throw new TripPreconditionException("Passenger requests can change only before a planned trip departs.");
    }
}
