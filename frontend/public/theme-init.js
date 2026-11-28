// Runs before the app bundle so the first paint uses the saved theme (mirrors applyTheme in
// src/shared/theme/theme.ts). A file rather than inline because the CSP has no 'unsafe-inline'.
;(function () {
  var pref = null
  try {
    pref = localStorage.getItem('itask.theme')
  } catch (e) {
    return
  }
  if (pref !== 'Dark' && pref !== 'Light') return
  document.documentElement.dataset.theme = pref === 'Dark' ? 'dark' : 'light'
  var metas = document.querySelectorAll('meta[name="theme-color"]')
  for (var i = 0; i < metas.length; i++) {
    var meta = metas[i]
    meta.dataset.own = meta.content
    meta.content = (pref === 'Dark' ? meta.dataset.dark : meta.dataset.light) || meta.content
  }
})()
