(function () {
  'use strict';
  var PAGE = 36;
  var main = document.getElementById('main');
  var view = localStorage.getItem('youfin.view') || 'grid';

  function creds() {
    try {
      var c = JSON.parse(localStorage.getItem('jellyfin_credentials') || '{}');
      var sid = (new URLSearchParams(location.search)).get('server');
      var s = (c.Servers || []).filter(function (x) { return !sid || x.Id === sid; })[0] || (c.Servers || [])[0];
      return s && s.AccessToken ? { token: s.AccessToken, uid: s.UserId } : null;
    } catch (e) { return null; }
  }
  var C = creds();
  var isAdmin = false;
  var hdr = function () { return { Authorization: 'MediaBrowser Client="Youfin", Device="Web", DeviceId="youfin-web", Version="0.1", Token="' + C.token + '"' }; };
  function api(path, opts) {
    opts = opts || {}; opts.headers = Object.assign(hdr(), opts.headers || {});
    return fetch(path, opts).then(function (r) { if (!r.ok) throw new Error(r.status); return r.status === 204 ? null : r.json(); });
  }
  function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
  function img(id, w) { return '/Items/' + id + '/Images/Primary?maxWidth=' + w + '&api_key=' + encodeURIComponent(C.token); }
  function dur(t) { if (!t) return ''; var s = Math.round(t / 1e7), h = Math.floor(s / 3600), m = Math.floor(s % 3600 / 60), x = s % 60; return (h ? h + ':' + String(m).padStart(2, '0') : m) + ':' + String(x).padStart(2, '0'); }
  function date(d) { return d ? new Date(d).toLocaleDateString('de-DE') : ''; }

  function card(v) {
    var pct = v.PositionTicks && v.RuntimeTicks ? Math.min(100, v.PositionTicks / v.RuntimeTicks * 100) : 0;
    return '<div class="card"><a class="thumb" href="#/video/' + v.ItemId + '"><img loading="lazy" src="' + img(v.ItemId, 480) + '" alt="">' +
      (v.RuntimeTicks ? '<span class="dur">' + dur(v.RuntimeTicks) + '</span>' : '') + (pct ? '<span class="bar" style="width:' + pct + '%"></span>' : '') + '</a>' +
      '<div><h3><a href="#/video/' + v.ItemId + '">' + esc(v.Title) + '</a></h3><div class="meta"><a href="#/channel/' + encodeURIComponent(v.ChannelKey) + '">' + esc(v.ChannelName) + '</a><br>' + date(v.Published) + '</div></div></div>';
  }
  function chanCard(c) {
    return '<a class="chan" href="#/channel/' + encodeURIComponent(c.Key) + '"><div class="av">' + (c.SampleVideoId ? '<img loading="lazy" src="' + img(c.SampleVideoId, 240) + '" alt="">' : '▶') + '</div><h3>' + esc(c.Name) + '</h3><div class="meta">' + c.VideoCount + ' Videos' + (c.IsFavorite ? ' <span class="star">★</span>' : '') + '</div></a>';
  }
  function q(o) { return Object.keys(o).filter(function (k) { return o[k] != null && o[k] !== ''; }).map(function (k) { return k + '=' + encodeURIComponent(o[k]); }).join('&'); }

  // Paged grid with "Mehr laden"; load(start) -> Promise<{Items,Total}>, render(item) -> html
  function paged(el, load, render, cls) {
    var start = 0;
    el.innerHTML = '<div class="grid ' + (cls || (view === 'list' ? 'list' : '')) + '"></div><a class="btn more" style="display:none">Mehr laden</a>';
    var grid = el.firstChild, more = el.lastChild;
    function next() {
      load(start).then(function (p) {
        if (!start && !p.Items.length) { el.innerHTML = '<div class="empty">Keine Einträge.</div>'; return; }
        grid.insertAdjacentHTML('beforeend', p.Items.map(render).join(''));
        start += p.Items.length; more.style.display = start < p.Total ? '' : 'none';
      }).catch(fail);
    }
    more.onclick = next;
    new IntersectionObserver(function (e) { if (e[0].isIntersecting && more.style.display !== 'none') next(); }).observe(more);
    next();
  }
  function fail(e) { main.innerHTML = '<div class="empty">Fehler: ' + esc(e.message) + '</div>'; }

  function videosPage(title, opts) {
    var sort = opts.sort || 'Added';
    main.innerHTML = '<div class="row"><h2>' + title + '</h2><span>' +
      (opts.sortable ? '<select id="sort"><option value="Added">Zuletzt hinzugefügt</option><option value="Published">Veröffentlichung</option><option value="Title">Titel</option></select> ' : '') +
      '<select id="view"><option value="grid">Raster</option><option value="list">Liste</option></select></span></div><div id="list"></div>';
    var list = document.getElementById('list');
    function go() {
      paged(list, function (s) { return api('/Youfin/Videos?' + q({ channel: opts.channel, filter: opts.filter, q: opts.q, category: opts.category, playlist: opts.playlist, sort: sort, desc: opts.asc ? 'false' : 'true', startIndex: s, limit: PAGE })); }, opts.playlist ? function (v) { return card(v).replace(/<\/div><\/div>$/, '<button class="btn rm" data-rm="' + v.ItemId + '">Entfernen</button></div></div>'); } : card);
      if (opts.playlist) list.onclick = function (e) { var id = e.target.dataset && e.target.dataset.rm; if (id) api('/Youfin/Playlists/' + opts.playlist + '/Videos/' + id, { method: 'DELETE' }).then(go); };
    }
    var sel = document.getElementById('sort');
    if (sel) { sel.value = sort; sel.onchange = function () { sort = sel.value; go(); }; }
    var vs = document.getElementById('view'); vs.value = view;
    vs.onchange = function () { view = vs.value; localStorage.setItem('youfin.view', view); go(); };
    go();
  }

  var routes = {
    home: function () {
      main.innerHTML = '<div id="mc"></div>';
      api('/Youfin/Channels?favorites=true&limit=12').then(function (p) { if (p.Items.length) document.getElementById('mc').innerHTML = '<div class="row"><h2>Meine Kanäle</h2><a class="btn" href="#/mychannels">Alle</a></div><div class="grid chans">' + p.Items.map(chanCard).join('') + '</div>'; });
      [['Weiter ansehen', 'resume', 'filter=resume'], ['Neu hinzugefügt', 'new', 'sort=Added'], ['Zuletzt angesehen', 'history', 'filter=recent']].forEach(function (s) {
        var h = document.createElement('div'); h.innerHTML = '<div class="row"><h2>' + s[0] + '</h2><a class="btn" href="#/' + s[1] + '">Alle</a></div><div></div>'; main.appendChild(h);
        api('/Youfin/Videos?' + s[2] + '&limit=8').then(function (p) { h.lastChild.innerHTML = p.Items.length ? '<div class="grid">' + p.Items.map(card).join('') + '</div>' : '<div class="empty">Keine Einträge.</div>'; });
      });
    },
    channels: function () {
      main.innerHTML = '<div class="row"><h2>Kanäle</h2><input id="cs" placeholder="Kanal suchen" class="btn"></div><div id="list"></div>';
      var t, list = document.getElementById('list');
      function go() { paged(list, function (s) { return api('/Youfin/Channels?' + q({ search: document.getElementById('cs').value, startIndex: s, limit: PAGE })); }, chanCard, 'chans'); }
      document.getElementById('cs').oninput = function () { clearTimeout(t); t = setTimeout(go, 250); };
      go();
    },
    channel: function (key) {
      main.innerHTML = '<div class="chead"><div class="av" id="cav">▶</div><div><h2 id="cname" style="margin:0"></h2><div class="meta" id="ccount"></div></div></div><div class="actions"><a class="btn pri" id="playall">Chronologisch ansehen</a><button class="btn" id="cfav">☆ Favorit</button>' + (isAdmin ? '<button class="btn" id="cren">Umbenennen</button><button class="btn" id="cmerge">Zusammenführen…</button>' : '') + '</div><div id="cv"></div>';
      api('/Youfin/Videos?' + q({ channel: key, sort: 'Published', desc: 'false', limit: 1 })).then(function (p) {
        if (!p.Items.length) return;
        document.getElementById('cname').textContent = p.Items[0].ChannelName; document.getElementById('ccount').textContent = p.Total + ' Videos';
        document.getElementById('cav').innerHTML = '<img src="' + img(p.Items[0].ItemId, 240) + '" alt="">';
        document.getElementById('playall').href = '#/video/' + p.Items[0].ItemId;
      });
      var ek = encodeURIComponent(key), jh = { 'Content-Type': 'application/json' };
      api('/Youfin/Channels?favorites=true&limit=200').then(function (p) {
        var fav = p.Items.some(function (c) { return c.Key === key; }), b = document.getElementById('cfav');
        function paint() { b.textContent = fav ? '★ Favorit' : '☆ Favorit'; }
        paint(); b.onclick = function () { api('/Youfin/Channels/' + ek + '/Favorite', { method: fav ? 'DELETE' : 'POST' }).then(function () { fav = !fav; paint(); }); };
      });
      var rn = document.getElementById('cren'), mg = document.getElementById('cmerge');
      if (rn) rn.onclick = function () { var n = prompt('Neuer Anzeigename (leer = Original)', document.getElementById('cname').textContent); if (n !== null) api('/Youfin/Channels/' + ek + '/Name', { method: 'PUT', headers: jh, body: JSON.stringify({ Name: n }) }).then(route); };
      if (mg) mg.onclick = function () { api('/Youfin/Channels?limit=200').then(function (p) {
        var l = p.Items.filter(function (c) { return c.Key !== key; }); var n = prompt('In welchen Kanal zusammenführen? Nummer eingeben:\n' + l.map(function (c, i) { return (i + 1) + ' ' + c.Name; }).join('\n'));
        var t = l[parseInt(n, 10) - 1]; if (t) api('/Youfin/Channels/' + ek + '/Merge', { method: 'POST', headers: jh, body: JSON.stringify({ Into: t.Key }) }).then(function () { location.hash = '#/channel/' + encodeURIComponent(t.Key); }); }); };
      var cv = document.getElementById('cv');
      var sort = 'Published';
      cv.innerHTML = '<div class="row"><span></span><select id="sort"><option value="Published">Neueste zuerst</option><option value="PublishedAsc">Älteste zuerst</option><option value="Title">Titel</option></select></div><div id="cl"></div>';
      var sel = document.getElementById('sort');
      function go() {
        var asc = sel.value === 'PublishedAsc', s = asc ? 'Published' : sel.value;
        paged(document.getElementById('cl'), function (st) { return api('/Youfin/Videos?' + q({ channel: key, sort: s, desc: s === 'Title' ? 'false' : String(!asc), startIndex: st, limit: PAGE })); }, card);
      }
      sel.onchange = go; go();
    },
    mychannels: function () {
      main.innerHTML = '<h2>Meine Kanäle</h2><div id="list"></div>';
      paged(document.getElementById('list'), function (s) { return api('/Youfin/Channels?' + q({ favorites: 'true', startIndex: s, limit: PAGE })); }, chanCard, 'chans');
    },
    favorites: function () { videosPage('Favoriten', { filter: 'favorites' }); },
    search: function (t) {
      main.innerHTML = '<h2>Suche: ' + esc(t) + '</h2><h3>Kanäle</h3><div id="sc"></div><h3>Videos</h3><div id="sv"></div>';
      api('/Youfin/Channels?' + q({ search: t, limit: 12 })).then(function (p) { document.getElementById('sc').innerHTML = p.Items.length ? '<div class="grid chans">' + p.Items.map(chanCard).join('') + '</div>' : '<div class="empty">Keine Kanäle.</div>'; });
      paged(document.getElementById('sv'), function (s) { return api('/Youfin/Videos?' + q({ q: t, sort: 'Published', startIndex: s, limit: PAGE })); }, card);
    },
    categories: function () {
      main.innerHTML = '<h2>Kategorien</h2><div class="tags" id="cats"></div>';
      api('/Youfin/Categories').then(function (l) { document.getElementById('cats').innerHTML = l.length ? l.map(function (c) { return '<a href="#/category/' + encodeURIComponent(c.Name) + '">' + esc(c.Name) + ' (' + c.VideoCount + ')</a>'; }).join('') : '<div class="empty">Keine Kategorien (info.json mit "categories" nötig).</div>'; });
    },
    category: function (n) { videosPage('Kategorie: ' + esc(n), { category: n, sortable: true }); },
    playlists: function () {
      main.innerHTML = '<div class="row"><h2>Playlists</h2><button class="btn pri" id="np">+ Neue Playlist</button></div><div class="tags" id="pls"></div>';
      function load() { api('/Youfin/Playlists').then(function (l) { document.getElementById('pls').innerHTML = l.length ? l.map(function (p) { return '<a href="#/playlist/' + p.Id + '">' + esc(p.Name) + ' (' + p.VideoCount + ')</a>'; }).join('') : '<div class="empty">Noch keine Playlists.</div>'; }); }
      document.getElementById('np').onclick = function () { var n = prompt('Name der Playlist'); if (n) api('/Youfin/Playlists', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ Name: n }) }).then(load); };
      load();
    },
    playlist: function (id) {
      videosPage('Playlist', { playlist: id, asc: true });
      var b = document.createElement('button'); b.className = 'btn'; b.textContent = 'Playlist löschen';
      b.onclick = function () { if (confirm('Playlist löschen?')) api('/Youfin/Playlists/' + id, { method: 'DELETE' }).then(function () { location.hash = '#/playlists'; }); };
      main.appendChild(b);
    },
    videos: function () { videosPage('Alle Videos', { sortable: true }); },
    new: function () { videosPage('Neu hinzugefügt', { sort: 'Added' }); },
    resume: function () { videosPage('Weiter ansehen', { filter: 'resume' }); },
    history: function () { videosPage('Zuletzt angesehen', { filter: 'recent' }); },
    video: function (id) {
      Promise.all([api('/Users/' + C.uid + '/Items/' + id), api('/Youfin/Videos/' + id).catch(function () { return null; })])
        .then(function (r) { render(r[0], r[1]); }).catch(fail);
      function render(it, me) {
        var ch = me ? me.ChannelKey : null, play = '/web/#/details?id=' + id;
        main.innerHTML = '<div class="detail"><div><a class="hero" href="' + play + '"><img src="' + img(id, 1280) + '" alt=""><span class="play">▶</span></a>' +
          '<h2 style="margin-top:14px">' + esc(it.Name) + '</h2><div class="meta">' + [me ? '<a href="#/channel/' + encodeURIComponent(ch) + '">' + esc(me.ChannelName) + '</a>' : '', date(it.PremiereDate), dur(it.RunTimeTicks)].filter(Boolean).join(' · ') + '</div>' +
          '<div class="actions"><a class="btn pri" href="' + play + '">Abspielen</a><button class="btn" id="prev">⏮ Vorheriges</button><button class="btn" id="next">Nächstes ⏭</button>' +
          '<button class="btn" id="played">' + (it.UserData && it.UserData.Played ? '✓ Gesehen' : 'Als gesehen markieren') + '</button>' +
          '<button class="btn" id="pl">+ Playlist</button><button class="btn" id="fav">' + (it.UserData && it.UserData.IsFavorite ? '★ Favorit' : '☆ Favorit') + '</button></div>' +
          '<div class="desc clamp" id="desc">' + esc(it.Overview || 'Keine Beschreibung.') + '</div><button class="btn" id="tog" style="margin-top:8px">Mehr anzeigen</button></div>' +
          '<div><h2>Weitere Videos dieses Kanals</h2><div class="grid list" id="more"></div></div></div>';
        document.getElementById('tog').onclick = function () { var d = document.getElementById('desc'); d.classList.toggle('clamp'); this.textContent = d.classList.contains('clamp') ? 'Mehr anzeigen' : 'Weniger anzeigen'; };
        document.getElementById('pl').onclick = function () {
          api('/Youfin/Playlists').then(function (l) {
            if (!l.length) { alert('Zuerst unter "Playlists" eine Playlist anlegen.'); return; }
            var n = prompt('Zu welcher Playlist? Nummer eingeben:\n' + l.map(function (p, i) { return (i + 1) + ' ' + p.Name; }).join('\n')), t = l[parseInt(n, 10) - 1];
            if (t) api('/Youfin/Playlists/' + t.Id + '/Videos/' + id, { method: 'POST' }).then(function () { alert('Hinzugefügt: ' + t.Name); });
          });
        };
        var played = !!(it.UserData && it.UserData.Played), fav = !!(it.UserData && it.UserData.IsFavorite);
        document.getElementById('played').onclick = function () { var b = this; api('/Users/' + C.uid + '/PlayedItems/' + id, { method: played ? 'DELETE' : 'POST' }).then(function () { played = !played; b.textContent = played ? '✓ Gesehen' : 'Als gesehen markieren'; }); };
        document.getElementById('fav').onclick = function () { var b = this; api('/Users/' + C.uid + '/FavoriteItems/' + id, { method: fav ? 'DELETE' : 'POST' }).then(function () { fav = !fav; b.textContent = fav ? '★ Favorit' : '☆ Favorit'; }); };
        if (!ch) { document.getElementById('prev').disabled = document.getElementById('next').disabled = true; return; }
        api('/Youfin/Videos?' + q({ channel: ch, sort: 'Published', desc: 'false', limit: 200 })).then(function (p) {
          var i = p.Items.map(function (x) { return x.ItemId; }).indexOf(id);
          var pv = p.Items[i - 1], nx = p.Items[i + 1];
          document.getElementById('prev').disabled = !pv; document.getElementById('next').disabled = !nx;
          if (pv) document.getElementById('prev').onclick = function () { location.hash = '#/video/' + pv.ItemId; };
          if (nx) document.getElementById('next').onclick = function () { location.hash = '#/video/' + nx.ItemId; };
          document.getElementById('more').innerHTML = p.Items.filter(function (x) { return x.ItemId !== id; }).slice(0, 12).map(card).join('');
        });
      }
    }
  };

  function route() {
    document.body.classList.remove('shownav');
    var h = (location.hash || '#/').slice(2).split('/'), name = h[0] || 'home', arg = h[1] ? decodeURIComponent(h[1]) : null;
    document.querySelectorAll('nav a').forEach(function (a) { a.classList.toggle('on', a.dataset.r === name); });
    if (!C) { main.innerHTML = '<div class="empty">Bitte zuerst in <a href="/web/"><u>Jellyfin</u></a> anmelden.</div>'; return; }
    (routes[name] || routes.home)(arg);
    window.scrollTo(0, 0);
  }
  document.getElementById('sf').onsubmit = function (e) { e.preventDefault(); var t = document.getElementById('sq').value.trim(); if (t) location.hash = '#/search/' + encodeURIComponent(t); };
  document.getElementById('menu').onclick = function () { document.body.classList.toggle(window.innerWidth > 900 ? 'nonav' : 'shownav'); };
  window.addEventListener('hashchange', route);
  if (C) { api('/Users/' + C.uid).then(function (u) { isAdmin = !!(u.Policy && u.Policy.IsAdministrator); }).catch(function () { }).then(route); } else { route(); }
})();
