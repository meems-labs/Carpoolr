using Carpoolr.Api.Contracts.Trips.V1;
using Carpoolr.Api.Features;
using Carpoolr.Api.Features.Bookings;
using Carpoolr.Api.Security;
using global::Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace Carpoolr.Api.Grpc;

[Authorize(Policy = AuthenticationExtensions.ColleaguePolicy)]
public sealed class BookingGrpcService(BookingApplicationService bookings, ICurrentUser user, TimeProvider clock)
    : BookingService.BookingServiceBase
{
    public override async Task<BookingResponse> RequestSeat(RequestSeatRequest request, ServerCallContext context) =>
        Response(await bookings.RequestAsync(ApplicationRules.Id(request.TripId, "trip_id"),
            ApplicationRules.Id(request.RequestId, "request_id"), user.GetRequiredIdentity(), context.CancellationToken));
    public override async Task<BookingResponse> AcceptBooking(ChangeBookingRequest request, ServerCallContext context) =>
        Response(await bookings.AcceptAsync(ApplicationRules.Id(request.BookingId, "booking_id"),
            user.GetRequiredIdentity(), context.CancellationToken));
    public override async Task<BookingResponse> RejectBooking(ChangeBookingRequest request, ServerCallContext context) =>
        Response(await bookings.RejectAsync(ApplicationRules.Id(request.BookingId, "booking_id"),
            user.GetRequiredIdentity(), context.CancellationToken));
    public override async Task<BookingResponse> WithdrawBooking(ChangeBookingRequest request, ServerCallContext context) =>
        Response(await bookings.WithdrawAsync(ApplicationRules.Id(request.BookingId, "booking_id"),
            user.GetRequiredIdentity(), context.CancellationToken));
    public override async Task<ListMyBookingsResponse> ListMyBookings(ListMyBookingsRequest request, ServerCallContext context)
    {
        var actor = user.GetRequiredIdentity();
        var page = await bookings.ListMineAsync(actor, request.Offset, request.PageSize, context.CancellationToken);
        return PageResponse(page, actor);
    }

    public override async Task<ListMyBookingsResponse> ListIncomingBookings(
        ListIncomingBookingsRequest request, ServerCallContext context)
    {
        var actor = user.GetRequiredIdentity();
        var page = await bookings.ListIncomingAsync(actor, request.Offset, request.PageSize,
            request.PendingOnly, context.CancellationToken);
        return PageResponse(page, actor);
    }

    private ListMyBookingsResponse PageResponse(BookingPage page, UserIdentity actor)
    {
        var now = clock.GetUtcNow();
        var response = new ListMyBookingsResponse();
        response.Entries.AddRange(page.Entries.Select(entry => new BookingWithTrip
        {
            Booking = TripWireMapper.MapBooking(entry.Booking, entry.Trip, actor, now),
            Trip = TripWireMapper.Map(entry.Trip, actor, now),
        }));
        if (page.NextOffset is { } offset) response.NextOffset = offset;
        return response;
    }
    private BookingResponse Response(BookingDetails details) => new()
    {
        Booking = TripWireMapper.MapBooking(details.Booking, details.Trip, user.GetRequiredIdentity(), clock.GetUtcNow()),
    };
}
