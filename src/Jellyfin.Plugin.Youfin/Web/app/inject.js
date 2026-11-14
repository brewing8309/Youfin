// Loaded into the Jellyfin web client by the plugin (only when libraries are chosen in the Youfin settings).
// When one of those libraries is opened, the Youfin interface is shown below the Jellyfin header.
(function () {
  'use strict';
  if (window.__youfinInject) return;
  window.__youfinInject = true;

  var src = document.currentScript && document.currentScript.src ? new URL(document.currentScript.src).pathname : '';
  var BASE = src.indexOf('/Youfin/Web/') >= 0 ? src.slice(0, src.indexOf('/Youfin/Web/')) : '';
  var cfg = null, dict = {}, fallback = {}, wrap = null, sw = null, prevOverflow = '';

  function norm(id) { return String(id || '').replace(/-/g, '').toLowerCase(); }
  function tr(key) { return dict[key] !== undefined ? dict[key] : (fallback[key] !== undefined ? fallback[key] : key); }
  function jget(url) { return fetch(BASE + url).then(function (r) { return r.ok ? r.json() : null; }).catch(function () { return null; }); }
  function loadTexts() {
    return jget('/Youfin/Web/languages.json').then(function (langs) {
      var codes = (langs || [{ code: 'en' }]).map(function (l) { return l.code.toLowerCase(); }), pick = 'en', saved = localStorage.getItem('youfin.lang');
      var prefs = saved ? [saved] : (navigator.languages && navigator.languages.length ? navigator.languages : [navigator.language || 'en']);
      for (var i = 0; i < prefs.length; i++) {
        var lc = String(prefs[i]).toLowerCase(), base = lc.split('-')[0];
        if (codes.indexOf(lc) >= 0) { pick = lc; break; }
        if (codes.indexOf(base) >= 0) { pick = base; break; }
      }
      return Promise.all([jget('/Youfin/Web/i18n/en.json'), pick === 'en' ? null : jget('/Youfin/Web/i18n/' + pick + '.json')]);
    }).then(function (r) { fallback = r[0] || {}; dict = r[1] || {}; });
  }

  // library id from the current jellyfin-web route, e.g. #/homevideos?topParentId=<id>
  function libraryFromRoute() {
    var h = location.hash || '', q = h.indexOf('?');
    if (q < 0) return null;
    var p = new URLSearchParams(h.slice(q + 1)), id = p.get('topParentId') || p.get('parentId');
    return id ? norm(id) : null;
  }
  function offList() { try { return JSON.parse(sessionStorage.getItem('youfin.native') || '[]'); } catch (e) { return []; } }

  // The frame starts below the first row of Jellyfin's header (navigation). In Jellyfin 12 the library toolbar is a second
  // header row; it is covered, because its sorting/filter buttons do not apply to the Youfin view.
  function headerBottom() {
    var bar = document.querySelector('header.MuiAppBar-root');
    if (bar) { var tb = bar.querySelector('.MuiToolbar-root'); return Math.max(0, Math.round((tb || bar).getBoundingClientRect().bottom)); }
    var h = document.querySelector('.skinHeader'), b = h ? Math.round(h.getBoundingClientRect().bottom) : 0;
    return b > 0 ? b : 48;
  }
  function place() { if (wrap) wrap.style.top = headerBottom() + 'px'; }

  function showFrame(lib) {
    if (wrap && wrap.getAttribute('data-lib') === lib) { place(); return; }
    removeFrame();
    wrap = document.createElement('div');
    wrap.id = 'youfin-embed'; wrap.setAttribute('data-lib', lib);
    wrap.style.cssText = 'position:fixed;left:0;right:0;bottom:0;z-index:1200;background:#0f0f0f';
    var f = document.createElement('iframe');
    f.src = BASE + '/Youfin/Web/?embed=1&lib=' + encodeURIComponent(lib);
    f.style.cssText = 'border:0;width:100%;height:100%;display:block';
    f.setAttribute('allowfullscreen', '');
    wrap.appendChild(f);
    document.body.appendChild(wrap);
    prevOverflow = document.body.style.overflow; document.body.style.overflow = 'hidden';
    place();
  }
  function removeFrame() { if (wrap) { wrap.remove(); wrap = null; document.body.style.overflow = prevOverflow; } }

  // after choosing "Jellyfin view" a small button brings the Youfin view back
  function showSwitch(lib) {
    if (sw) return;
    sw = document.createElement('button');
    sw.textContent = tr('inject.openYoufin');
    sw.style.cssText = 'position:fixed;right:16px;bottom:16px;z-index:1200;background:#00a4dc;color:#0f0f0f;border:0;border-radius:20px;padding:10px 18px;font:600 14px system-ui,sans-serif;cursor:pointer;box-shadow:0 4px 14px #0008';
    sw.onclick = function () { sessionStorage.setItem('youfin.native', JSON.stringify(offList().filter(function (x) { return x !== lib; }))); update(); };
    document.body.appendChild(sw);
  }
  function removeSwitch() { if (sw) { sw.remove(); sw = null; } }

  function update() {
    if (!cfg) return;
    var id = libraryFromRoute(), included = !!id && cfg.LibraryIds.indexOf(id) >= 0, off = included && offList().indexOf(id) >= 0;
    if (included && !off) showFrame(id); else removeFrame();
    if (included && off) showSwitch(id); else removeSwitch();
  }

  function load(token) {
    fetch(BASE + '/Youfin/Settings', { headers: { Authorization: 'MediaBrowser Token="' + token + '"' } })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (s) {
        if (!s || !s.ReplaceLibraryView || !s.LibraryIds || !s.LibraryIds.length) return;
        cfg = { LibraryIds: s.LibraryIds.map(norm) };
        return loadTexts();
      })
      .then(function () { if (cfg) { window.addEventListener('hashchange', update); window.addEventListener('resize', place); update(); } })
      .catch(function () { /* never break jellyfin-web */ });
  }
  // the sign-in token is stored by jellyfin-web in localStorage (same origin), like the Youfin interface reads it
  function token() {
    try {
      var c = JSON.parse(localStorage.getItem('jellyfin_credentials') || '{}'), s = (c.Servers || [])[0];
      if (s && s.AccessToken) return s.AccessToken;
    } catch (e) { /* fall through */ }
    var ac = window.ApiClient;
    return ac && ac.accessToken ? ac.accessToken() : null;
  }
  (function wait(tries) {
    var t = token();
    if (t) load(t);
    else if (tries < 120) setTimeout(function () { wait(tries + 1); }, 500);
  })(0);
})();
