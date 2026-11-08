// Runs before styles and Vue so the first paint uses the saved device preference.
;(function () {
  var preference = 'system'
  try {
    var saved = localStorage.getItem('carpoolr.theme')
    if (saved === 'light' || saved === 'dark') preference = saved
  } catch (_) {
    // Storage may be disabled; the system preference still works.
  }
  var dark =
    preference === 'dark' ||
    (preference === 'system' && matchMedia('(prefers-color-scheme: dark)').matches)
  document.documentElement.dataset.theme = dark ? 'dark' : 'light'
  document.documentElement.style.colorScheme = dark ? 'dark' : 'light'
  var meta = document.querySelector('meta[name="theme-color"]')
  if (meta) meta.content = dark ? '#111b17' : '#f5f4ef'
})()
