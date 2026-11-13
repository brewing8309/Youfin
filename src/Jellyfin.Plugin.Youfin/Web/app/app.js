(function () {
  'use strict';
  var PAGE = 36;
  var main = document.getElementById('main');
  var view = localStorage.getItem('youfin.view') || 'grid';
  var isAdmin = false;
  var vc = {};            // video cache: id -> dto (for the ⋯ menu)
  var sel = {};           // selected video ids
  var selMode = false;

  // ---------- helpers ----------
  function creds() {
    try {
      var c = JSON.parse(localStorage.getItem('jellyfin_credentials') || '{}');
      var sid = (new URLSearchParams(location.search)).get('server');
      var s = (c.Servers || []).filter(function (x) { return !sid || x.Id === sid; })[0] || (c.Servers || [])[0];
      return s && s.AccessToken ? { token: s.AccessToken, uid: s.UserId } : null;
    } catch (e) { return null; }
  }
  var C = creds();
  function api(path, opts) {
    opts = opts || {};
    opts.headers = Object.assign({ Authorization: 'MediaBrowser Client="Youfin", Device="Web", DeviceId="youfin-web", Version="0.1", Token="' + C.token + '"' }, opts.headers || {});
    return fetch(path, opts).then(function (r) { if (!r.ok) throw new Error(r.status === 403 ? 'Keine Berechtigung' : r.status === 409 ? 'Existiert bereits' : 'Fehler ' + r.status); return r.status === 204 ? null : r.json(); });
  }
  function send(method, path, body) {
    return api(path, { method: method, headers: { 'Content-Type': 'application/json' }, body: body == null ? undefined : JSON.stringify(body) });
  }
  function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
  function img(id, w) { return '/Items/' + id + '/Images/Primary?maxWidth=' + w + '&api_key=' + encodeURIComponent(C.token); }
  function dur(t) { if (!t) return ''; var s = Math.round(t / 1e7), h = Math.floor(s / 3600), m = Math.floor(s % 3600 / 60), x = s % 60; return (h ? h + ':' + String(m).padStart(2, '0') : m) + ':' + String(x).padStart(2, '0'); }
  function date(d) { return d ? new Date(d).toLocaleDateString('de-DE') : ''; }
  function q(o) { return Object.keys(o).filter(function (k) { return o[k] != null && o[k] !== ''; }).map(function (k) { return k + '=' + encodeURIComponent(o[k]); }).join('&'); }
  function $(id) { return document.getElementById(id); }
  function fail(e) { main.innerHTML = '<div class="empty">Fehler: ' + esc(e.message) + '</div>'; }
  function toast(msg) { var t = $('toast'); t.textContent = msg; t.hidden = false; clearTimeout(toast.t); toast.t = setTimeout(function () { t.hidden = true; }, 2500); }
  function ids(o) { return Object.keys(o); }

  function dialog(title, items) {
    return new Promise(function (resolve) {
      var m = document.createElement('div'); m.className = 'modal';
      m.innerHTML = '<div class="box"><h3>' + esc(title) + '</h3>' + items.map(function (it, i) { return '<button class="btn" data-i="' + i + '">' + esc(it.label) + '</button>'; }).join('') + '<button class="btn" data-i="-1">Abbrechen</button></div>';
      m.onclick = function (e) {
        var i = e.target.dataset ? e.target.dataset.i : undefined;
        if (e.target === m || i === '-1') { m.remove(); resolve(null); } else if (i !== undefined) { m.remove(); resolve(items[+i]); }
      };
      document.body.appendChild(m);
    });
  }
  function done(msg) { toast(msg); route(); }
  function guard(p) { return p.catch(function (e) { toast(e.message); }); }

  // ---------- video actions (⋯ menu / selection) ----------
  function addToCollection(list) {
    api('/Youfin/Collections').then(function (cols) {
      var items = cols.map(function (c) { return { label: c.Name + ' (' + c.VideoCount + ')', id: c.Id }; });
      items.push({ label: '＋ Neue Sammlung…', id: null });
      return dialog('Zu Sammlung hinzufügen', items).then(function (o) {
        if (!o) return;
        if (o.id) return guard(send('POST', '/Youfin/Collections/' + o.id + '/Videos', { VideoIds: list }).then(function () { toast('Hinzugefügt'); }));
        var n = prompt('Name der neuen Sammlung'); if (!n) return;
        return guard(send('POST', '/Youfin/Collections', { Name: n }).then(function (c) { return send('POST', '/Youfin/Collections/' + c.Id + '/Videos', { VideoIds: list }); }).then(function () { toast('Sammlung angelegt'); }));
      });
    }).catch(function (e) { toast(e.message); });
  }
  function addToSeries(list) {
    api('/Youfin/Series?limit=200').then(function (p) {
      if (!p.Items.length) { toast('Noch keine Serie vorhanden'); return; }
      return dialog('Zu Serie hinzufügen', p.Items.map(function (s) { return { label: s.Name + ' (' + s.VideoCount + ')', id: s.Id }; })).then(function (o) {
        if (o) guard(send('POST', '/Youfin/Series/' + o.id + '/Videos', { VideoIds: list }).then(function () { done('Zur Serie hinzugefügt'); }));
      });
    }).catch(function (e) { toast(e.message); });
  }
  function newSeries(list) {
    var n = prompt('Name der neuen Serie'); if (!n) return;
    guard(send('POST', '/Youfin/Series', { Name: n, VideoIds: list }).then(function (s) { location.hash = '#/serie/' + s.Id; }));
  }
  function videoMenu(list) {
    var multi = list.length > 1, v = vc[list[0]] || {};
    var o = [
      { label: 'Später ansehen', run: function () { guard(send('POST', '/Youfin/Collections/watchlater/Videos', { VideoIds: list }).then(function () { toast('Zu „Später ansehen“ hinzugefügt'); })); } },
      { label: 'Zu Sammlung hinzufügen…', run: addToCollection }
    ];
    if (isAdmin) {
      o.push({ label: 'Zu Serie hinzufügen…', run: addToSeries }, { label: 'Neue Serie daraus…', run: newSeries });
      if (multi || v.SeriesId) o.push({ label: multi ? 'Als Einzelvideos (aus Serie lösen)' : 'Aus Serie lösen (Einzelvideo)', run: function (l) { guard(send('POST', '/Youfin/Videos/Single', { VideoIds: l }).then(function () { done('Als Einzelvideo gekennzeichnet'); })); } });
    }
    dialog(multi ? list.length + ' Videos' : (v.Title || 'Video'), o).then(function (x) { if (x) x.run(list); });
  }

  // ---------- selection ----------
  function setSel(on) {
    selMode = on; if (!on) sel = {};
    document.body.classList.toggle('selmode', on); updateSel();
    document.querySelectorAll('.card.picked').forEach(function (c) { c.classList.remove('picked'); });
  }
  function updateSel() { $('selbar').hidden = !selMode; $('selcount').textContent = ids(sel).length + ' ausgewählt'; }
  main.addEventListener('click', function (e) {
    var k = e.target.closest('[data-menu]');
    if (k) { e.preventDefault(); videoMenu([k.dataset.menu]); return; }
    if (selMode) {
      var c = e.target.closest('.card');
      if (c && c.dataset.id) { e.preventDefault(); if (sel[c.dataset.id]) { delete sel[c.dataset.id]; } else { sel[c.dataset.id] = 1; } c.classList.toggle('picked', !!sel[c.dataset.id]); updateSel(); }
    }
  });
  $('selact').onclick = function () { var l = ids(sel); if (l.length) videoMenu(l); else toast('Nichts ausgewählt'); };
  $('seldone').onclick = function () { setSel(false); };
  function selButton(parent) {
    var b = document.createElement('button'); b.className = 'btn'; b.textContent = 'Auswählen';
    b.onclick = function () { setSel(!selMode); }; parent.appendChild(b); return b;
  }

  // ---------- cards ----------
  function card(v, badge) {
    vc[v.ItemId] = v;
    var pct = v.PositionTicks && v.RuntimeTicks ? Math.min(100, v.PositionTicks / v.RuntimeTicks * 100) : 0;
    return '<div class="card' + (sel[v.ItemId] ? ' picked' : '') + '" data-id="' + v.ItemId + '"><a class="thumb" href="#/video/' + v.ItemId + '"><img loading="lazy" src="' + img(v.ItemId, 480) + '" alt="">' +
      (v.RuntimeTicks ? '<span class="dur">' + dur(v.RuntimeTicks) + '</span>' : '') + (pct ? '<span class="bar" style="width:' + pct + '%"></span>' : '') + '</a>' +
      '<button class="kebab" data-menu="' + v.ItemId + '" aria-label="Aktionen">⋯</button>' +
      '<div><h3>' + (badge ? '<span class="badge">' + esc(badge) + '</span>' : '') + '<a href="#/video/' + v.ItemId + '">' + esc(v.Title) + '</a></h3><div class="meta"><a href="#/channel/' + encodeURIComponent(v.ChannelKey) + '">' + esc(v.ChannelName) + '</a>' +
      (v.SeriesId && v.SeriesName !== v.ChannelName ? ' · <a href="#/serie/' + v.SeriesId + '">' + esc(v.SeriesName) + '</a>' : '') + '<br>' + date(v.Published) + '</div></div></div>';
  }
  function chanCard(c) {
    return '<a class="chan" href="#/channel/' + encodeURIComponent(c.Key) + '"><div class="av">' + (c.SampleVideoId ? '<img loading="lazy" src="' + img(c.SampleVideoId, 240) + '" alt="">' : '▶') + '</div><h3>' + esc(c.Name) + '</h3><div class="meta">' + c.VideoCount + ' Videos' + (c.IsFavorite ? ' <span class="star">★</span>' : '') + '</div></a>';
  }
  function serCard(s) {
    return '<div class="card"><a class="thumb" href="#/serie/' + s.Id + '">' + (s.FirstVideoId ? '<img loading="lazy" src="' + img(s.FirstVideoId, 480) + '" alt="">' : '') + '<span class="dur">' + s.VideoCount + ' Folgen</span></a><div><h3><a href="#/serie/' + s.Id + '">' + esc(s.Name) + '</a></h3><div class="meta">' + (s.AutoChannelKey ? 'Neue Videos des Kanals kommen automatisch dazu' : 'Manuell zusammengestellt') + '</div></div></div>';
  }

  // Paged grid with infinite scroll; load(start) -> Promise<{Items,Total}>
  function paged(el, load, render, cls, emptyMsg) {
    var start = 0;
    el.innerHTML = '<div class="grid ' + (cls || (view === 'list' ? 'list' : '')) + '"></div><a class="btn more" style="display:none">Mehr laden</a>';
    var grid = el.firstChild, more = el.lastChild;
    function next() {
      load(start).then(function (p) {
        if (!start && !p.Items.length) { el.innerHTML = '<div class="empty">' + (emptyMsg || 'Keine Einträge.') + '</div>'; return; }
        grid.insertAdjacentHTML('beforeend', p.Items.map(function (v) { return render(v); }).join(''));
        start += p.Items.length; more.style.display = start < p.Total ? '' : 'none';
      }).catch(fail);
    }
    more.onclick = next;
    new IntersectionObserver(function (e) { if (e[0].isIntersecting && more.style.display !== 'none') next(); }).observe(more);
    next();
  }

  // List with manual up/down order; items: dtos, save(ids), remove(id) optional
  function reorderable(el, items, save, remove) {
    function draw() {
      el.innerHTML = '<div class="grid list">' + items.map(function (v, i) {
        return '<div class="ord">' + card(v, '#' + (i + 1)) + '<div class="ordbtn"><button class="btn" data-up="' + i + '"' + (i ? '' : ' disabled') + '>↑</button><button class="btn" data-dn="' + i + '"' + (i < items.length - 1 ? '' : ' disabled') + '>↓</button>' + (remove ? '<button class="btn" data-rm="' + i + '">✕</button>' : '') + '</div></div>';
      }).join('') + '</div>';
    }
    el.onclick = function (e) {
      var d = e.target.dataset || {}, i;
      if (d.up !== undefined) { i = +d.up; items.splice(i - 1, 0, items.splice(i, 1)[0]); save(items.map(function (v) { return v.ItemId; })); draw(); }
      else if (d.dn !== undefined) { i = +d.dn; items.splice(i + 1, 0, items.splice(i, 1)[0]); save(items.map(function (v) { return v.ItemId; })); draw(); }
      else if (d.rm !== undefined) { var v = items.splice(+d.rm, 1)[0]; remove(v.ItemId); draw(); }
    };
    draw();
  }

  function videosPage(title, opts) {
    var sort = opts.sort || 'Added';
    main.innerHTML = '<div class="row"><h2>' + title + '</h2><span id="tools"></span></div><div id="list"></div>';
    var tools = $('tools'), list = $('list');
    if (opts.sortable) { tools.insertAdjacentHTML('beforeend', '<select id="sort"><option value="Added">Zuletzt hinzugefügt</option><option value="Published">Veröffentlichung</option><option value="Title">Titel</option></select> '); }
    tools.insertAdjacentHTML('beforeend', '<select id="view"><option value="grid">Raster</option><option value="list">Liste</option></select> ');
    selButton(tools);
    function go() {
      paged(list, function (s) { return api('/Youfin/Videos?' + q({ channel: opts.channel, filter: opts.filter, q: opts.q, category: opts.category, collection: opts.collection, single: opts.single, sort: sort, desc: opts.asc ? 'false' : 'true', startIndex: s, limit: PAGE })); }, card, null, opts.empty);
    }
    var sel1 = $('sort');
    if (sel1) { sel1.value = sort; sel1.onchange = function () { sort = sel1.value; go(); }; }
    var vs = $('view'); vs.value = view;
    vs.onchange = function () { view = vs.value; localStorage.setItem('youfin.view', view); go(); };
    go();
  }

  function row(parent, title, link, url, render) {
    var h = document.createElement('section');
    h.innerHTML = '<div class="row"><h2>' + title + '</h2>' + (link ? '<a class="btn" href="' + link + '">Alle</a>' : '') + '</div><div></div>';
    parent.appendChild(h);
    return api(url).then(function (p) {
      var items = Array.isArray(p) ? p : p.Items;
      if (!items || !items.length) { h.remove(); return; }
      h.lastChild.innerHTML = '<div class="grid">' + (render ? render(items) : items.map(function (v) { return card(v); }).join('')) + '</div>';
    }).catch(function () { h.remove(); });
  }

  // ---------- routes ----------
  var routes = {
    home: function () {
      main.innerHTML = '';
      row(main, 'Weiter ansehen', '#/resume', '/Youfin/Videos?filter=resume&limit=8');
      row(main, 'Nächste Folge deiner Serien', '#/series', '/Youfin/NextUp?limit=8');
      row(main, 'Neue Einzelvideos', '#/single', '/Youfin/Videos?single=true&sort=Added&limit=8');
      row(main, 'Später ansehen', '#/watchlater', '/Youfin/Videos?collection=watchlater&limit=8');
      row(main, 'Meine Kanäle', '#/mychannels', '/Youfin/Channels?favorites=true&limit=12', function (l) { return l.map(chanCard).join(''); });
      // random unplayed single videos, sampled client-side from one page
      row(main, 'Aus dem Archiv', null, '/Youfin/Videos?single=true&sort=Added&limit=200', function (items) {
        return items.filter(function (v) { return !v.Played; }).sort(function () { return Math.random() - .5; }).slice(0, 8).map(function (v) { return card(v); }).join('');
      });
      row(main, 'Zuletzt hinzugefügt', '#/new', '/Youfin/Videos?sort=Added&limit=8');
      row(main, 'Zuletzt angesehen', '#/history', '/Youfin/Videos?filter=recent&limit=8');
    },
    series: function () {
      main.innerHTML = '<div class="row"><h2>Serien</h2><span id="tools"></span></div><div class="sub">Zusammengehörige Videos in fester Reihenfolge. Einzelne Videos findest du unter „Einzelvideos“.</div><div id="list"></div>';
      if (isAdmin) { var b = document.createElement('button'); b.className = 'btn pri'; b.textContent = '+ Neue Serie'; b.onclick = function () { newSeries([]); }; $('tools').appendChild(b); }
      paged($('list'), function (s) { return api('/Youfin/Series?' + q({ startIndex: s, limit: PAGE })); }, serCard, null, 'Noch keine Serien. Auf einer Kanalseite kannst du eine Serie anlegen.');
    },
    serie: function (id) {
      Promise.all([api('/Youfin/Series/' + id), api('/Youfin/Videos?' + q({ series: id, sort: 'Series', limit: 200 }))]).then(function (r) {
        var s = r[0], items = r[1].Items, played = items.filter(function (v) { return v.Played; }).length;
        var next = items.filter(function (v) { return !v.Played; })[0];
        main.innerHTML = '<div class="row"><h2>' + esc(s.Name) + '</h2><span id="tools"></span></div><div class="sub">' + s.VideoCount + ' Folgen · ' + played + ' gesehen' +
          (s.AutoChannelKey ? ' · Neue Videos des Kanals kommen automatisch dazu' : '') + '</div><div class="actions" id="sact"></div><div id="list"></div>';
        var act = $('sact');
        if (next) act.insertAdjacentHTML('beforeend', '<a class="btn pri" href="#/video/' + next.ItemId + '">' + (played ? 'Nächste Folge' : 'Erste Folge') + ' ansehen</a>');
        if (isAdmin) {
          var ctl = function (label, fn) { var b = document.createElement('button'); b.className = 'btn'; b.textContent = label; b.onclick = fn; act.appendChild(b); };
          ctl('Umbenennen', function () { var n = prompt('Neuer Name', s.Name); if (n) guard(send('PUT', '/Youfin/Series/' + id, { Name: n }).then(function () { done('Gespeichert'); })); });
          ctl('Sortierung: ' + ({ published: 'Veröffentlichung', manual: 'Manuell', title: 'Titel' }[s.Sort] || s.Sort), function () {
            dialog('Sortierung', [{ label: 'Nach Veröffentlichung', v: 'published' }, { label: 'Nach Titel', v: 'title' }, { label: 'Manuell (Reihenfolge ändern)', v: 'manual' }]).then(function (o) { if (o) guard(send('PUT', '/Youfin/Series/' + id, { Sort: o.v }).then(function () { done('Gespeichert'); })); });
          });
          if (s.AutoChannelKey) ctl('Kanalregel entfernen', function () { guard(send('PUT', '/Youfin/Series/' + id, { AutoChannelKey: '' }).then(function () { done('Regel entfernt'); })); });
          if (items.length > 1 && s.VideoCount <= 200) ctl('Reihenfolge ändern', function () {
            reorderable($('list'), items.slice(), function (l) { guard(send('PUT', '/Youfin/Series/' + id + '/Order', { VideoIds: l })); });
            toast('Mit ↑ ↓ sortieren – wird automatisch gespeichert');
          });
          ctl('Serie löschen', function () { if (confirm('Serie „' + s.Name + '“ löschen? Die Videos bleiben erhalten und werden zu Einzelvideos.')) guard(send('DELETE', '/Youfin/Series/' + id).then(function () { location.hash = '#/series'; })); });
        }
        selButton($('tools'));
        $('list').innerHTML = '<div class="grid list">' + items.map(function (v, i) { return card(v, '#' + (i + 1)); }).join('') + '</div>' + (s.VideoCount > items.length ? '<div class="empty">Es werden die ersten 200 Folgen angezeigt.</div>' : '');
      }).catch(fail);
    },
    single: function () { videosPage('Einzelvideos', { single: 'true', sortable: true, empty: 'Keine Einzelvideos.' }); },
    channels: function () { chanList('Alle Kanäle', false); },
    mychannels: function () { chanList('Favoriten', true); },
    channel: function (key) {
      main.innerHTML = '<div class="chead"><div class="av" id="cav">▶</div><div><h2 id="cname" style="margin:0"></h2><div class="meta" id="ccount"></div></div></div><div class="actions"><a class="btn pri" id="playall">Chronologisch ansehen</a><button class="btn" id="cfav">☆ Favorit</button>' +
        (isAdmin ? '<button class="btn" id="cser">Als Serie anlegen</button><button class="btn" id="cren">Umbenennen</button><button class="btn" id="cmerge">Zusammenführen…</button>' : '') + '</div><div id="cv"></div>';
      var ek = encodeURIComponent(key);
      api('/Youfin/Videos?' + q({ channel: key, sort: 'Published', desc: 'false', limit: 1 })).then(function (p) {
        if (!p.Items.length) return;
        $('cname').textContent = p.Items[0].ChannelName; $('ccount').textContent = p.Total + ' Videos';
        $('cav').innerHTML = '<img src="' + img(p.Items[0].ItemId, 240) + '" alt="">';
        $('playall').href = '#/video/' + p.Items[0].ItemId;
      });
      api('/Youfin/Channels?favorites=true&limit=200').then(function (p) {
        var fav = p.Items.some(function (c) { return c.Key === key; }), b = $('cfav');
        function paint() { b.textContent = fav ? '★ Favorit' : '☆ Favorit'; }
        paint(); b.onclick = function () { api('/Youfin/Channels/' + ek + '/Favorite', { method: fav ? 'DELETE' : 'POST' }).then(function () { fav = !fav; paint(); }); };
      });
      var cs = $('cser'), rn = $('cren'), mg = $('cmerge');
      if (cs) cs.onclick = function () { if (confirm('Alle Videos dieses Kanals (auch künftige) zu einer Serie zusammenfassen?')) guard(send('POST', '/Youfin/Series', { AutoChannelKey: key }).then(function (s) { location.hash = '#/serie/' + s.Id; })); };
      if (rn) rn.onclick = function () { var n = prompt('Neuer Anzeigename (leer = Original)', $('cname').textContent); if (n !== null) guard(send('PUT', '/Youfin/Channels/' + ek + '/Name', { Name: n }).then(function () { done('Gespeichert'); })); };
      if (mg) mg.onclick = function () { api('/Youfin/Channels?limit=200').then(function (p) {
        var l = p.Items.filter(function (c) { return c.Key !== key; });
        return dialog('In welchen Kanal zusammenführen?', l.map(function (c) { return { label: c.Name + ' (' + c.VideoCount + ')', key: c.Key }; })).then(function (o) { if (o) guard(send('POST', '/Youfin/Channels/' + ek + '/Merge', { Into: o.key }).then(function () { location.hash = '#/channel/' + encodeURIComponent(o.key); })); }); }); };
      var sort = 'Published', cv = $('cv');
      cv.innerHTML = '<div class="row"><span id="tools"></span><select id="sort"><option value="Published">Neueste zuerst</option><option value="PublishedAsc">Älteste zuerst</option><option value="Title">Titel</option></select></div><div id="cl"></div>';
      selButton($('tools'));
      var s1 = $('sort');
      function go() {
        var asc = s1.value === 'PublishedAsc', s = asc ? 'Published' : s1.value;
        paged($('cl'), function (st) { return api('/Youfin/Videos?' + q({ channel: key, sort: s, desc: s === 'Title' ? 'false' : String(!asc), startIndex: st, limit: PAGE })); }, card);
      }
      s1.onchange = go; go();
    },
    videos: function () { videosPage('Alle Videos', { sortable: true }); },
    new: function () { videosPage('Neu hinzugefügt', { sort: 'Added' }); },
    resume: function () { videosPage('Weiter ansehen', { filter: 'resume' }); },
    history: function () { videosPage('Verlauf', { filter: 'recent' }); },
    favorites: function () { videosPage('Favoriten', { filter: 'favorites' }); },
    watchlater: function () { routes.collection('watchlater'); },
    search: function (t) {
      main.innerHTML = '<h2>Suche: ' + esc(t) + '</h2><h3>Serien</h3><div id="ss"></div><h3>Kanäle</h3><div id="sc"></div><h3>Videos</h3><div id="sv"></div>';
      api('/Youfin/Series?' + q({ search: t, limit: 12 })).then(function (p) { $('ss').innerHTML = p.Items.length ? '<div class="grid">' + p.Items.map(serCard).join('') + '</div>' : '<div class="empty">Keine Serien.</div>'; });
      api('/Youfin/Channels?' + q({ search: t, limit: 12 })).then(function (p) { $('sc').innerHTML = p.Items.length ? '<div class="grid chans">' + p.Items.map(chanCard).join('') + '</div>' : '<div class="empty">Keine Kanäle.</div>'; });
      paged($('sv'), function (s) { return api('/Youfin/Videos?' + q({ q: t, sort: 'Published', startIndex: s, limit: PAGE })); }, card);
    },
    categories: function () {
      main.innerHTML = '<h2>Kategorien</h2><div class="tags" id="cats"></div>';
      api('/Youfin/Categories').then(function (l) { $('cats').innerHTML = l.length ? l.map(function (c) { return '<a href="#/category/' + encodeURIComponent(c.Name) + '">' + esc(c.Name) + ' (' + c.VideoCount + ')</a>'; }).join('') : '<div class="empty">Keine Kategorien (info.json mit "categories" nötig).</div>'; });
    },
    category: function (n) { videosPage('Kategorie: ' + esc(n), { category: n, sortable: true }); },
    collections: function () {
      main.innerHTML = '<div class="row"><h2>Sammlungen</h2><button class="btn pri" id="nc">+ Neue Sammlung</button></div><div class="sub">Deine eigenen Gruppierungen, unabhängig von Kanal und Serie. Ein Video kann in mehreren Sammlungen sein.</div><div class="tags" id="cols"></div>';
      $('nc').onclick = function () { var n = prompt('Name der Sammlung'); if (n) guard(send('POST', '/Youfin/Collections', { Name: n }).then(function (c) { location.hash = '#/collection/' + c.Id; })); };
      api('/Youfin/Collections').then(function (l) { $('cols').innerHTML = l.map(function (c) { return '<a href="#/collection/' + c.Id + '">' + (c.Kind === 'watchLater' ? '⏱ ' : '') + esc(c.Name) + ' (' + c.VideoCount + ')</a>'; }).join(''); });
    },
    collection: function (id) {
      Promise.all([api('/Youfin/Collections'), api('/Youfin/Videos?' + q({ collection: id, limit: 200 }))]).then(function (r) {
        var c = r[0].filter(function (x) { return x.Id === id; })[0] || { Name: 'Später ansehen', Kind: 'watchLater' }, items = r[1].Items, later = c.Kind === 'watchLater';
        main.innerHTML = '<div class="row"><h2>' + esc(c.Name) + '</h2><span id="tools"></span></div><div class="sub">' + r[1].Total + ' Videos' + (later ? '' : ' · Reihenfolge frei änderbar') + '</div><div class="actions" id="cact"></div><div id="list"></div>';
        var act = $('cact');
        if (items.length) act.insertAdjacentHTML('beforeend', '<a class="btn pri" href="#/video/' + items[0].ItemId + '">Ab erstem Video ansehen</a>');
        if (!later) {
          var b1 = document.createElement('button'); b1.className = 'btn'; b1.textContent = 'Umbenennen'; b1.onclick = function () { var n = prompt('Neuer Name', c.Name); if (n) guard(send('PUT', '/Youfin/Collections/' + id, { Name: n }).then(function () { done('Gespeichert'); })); }; act.appendChild(b1);
          var b2 = document.createElement('button'); b2.className = 'btn'; b2.textContent = 'Sammlung löschen'; b2.onclick = function () { if (confirm('Sammlung löschen? Die Videos bleiben erhalten.')) guard(send('DELETE', '/Youfin/Collections/' + id).then(function () { location.hash = '#/collections'; })); }; act.appendChild(b2);
        }
        if (!items.length) { $('list').innerHTML = '<div class="empty">Noch keine Videos. Über „⋯“ an einem Video hinzufügen.</div>'; return; }
        reorderable($('list'), items, function (l) { guard(send('PUT', '/Youfin/Collections/' + id + '/Order', { VideoIds: l })); }, function (vid) { guard(api('/Youfin/Collections/' + id + '/Videos/' + vid, { method: 'DELETE' })); });
      }).catch(fail);
    },
    video: function (id) {
      Promise.all([api('/Users/' + C.uid + '/Items/' + id), api('/Youfin/Videos/' + id).catch(function () { return null; })]).then(function (r) { render(r[0], r[1]); }).catch(fail);
      function render(it, me) {
        if (me) vc[id] = me;
        var ch = me ? me.ChannelKey : null, play = '/web/#/details?id=' + id, sid = me && me.SeriesId;
        main.innerHTML = '<div class="detail"><div><a class="hero" href="' + play + '"><img src="' + img(id, 1280) + '" alt=""><span class="play">▶</span></a>' +
          '<h2 style="margin-top:14px">' + esc(it.Name) + '</h2><div class="meta">' + [me ? '<a href="#/channel/' + encodeURIComponent(ch) + '">' + esc(me.ChannelName) + '</a>' : '', sid ? 'Serie: <a href="#/serie/' + sid + '">' + esc(me.SeriesName) + '</a>' : (me ? 'Einzelvideo' : ''), date(it.PremiereDate), dur(it.RunTimeTicks)].filter(Boolean).join(' · ') + '</div>' +
          '<div class="actions"><a class="btn pri" href="' + play + '">Abspielen</a><button class="btn" id="prev">⏮ Vorheriges</button><button class="btn" id="next">Nächstes ⏭</button>' +
          '<button class="btn" id="played">' + (it.UserData && it.UserData.Played ? '✓ Gesehen' : 'Als gesehen markieren') + '</button>' +
          '<button class="btn" id="fav">' + (it.UserData && it.UserData.IsFavorite ? '★ Favorit' : '☆ Favorit') + '</button><button class="btn" id="more" data-menu="' + id + '">⋯ Mehr</button></div>' +
          '<div class="desc clamp" id="desc">' + esc(it.Overview || 'Keine Beschreibung.') + '</div><button class="btn" id="tog" style="margin-top:8px">Mehr anzeigen</button></div>' +
          '<div><h2 id="relh">' + (sid ? 'Weitere Folgen' : 'Weitere Videos dieses Kanals') + '</h2><div class="grid list" id="rel"></div></div></div>';
        $('tog').onclick = function () { var d = $('desc'); d.classList.toggle('clamp'); this.textContent = d.classList.contains('clamp') ? 'Mehr anzeigen' : 'Weniger anzeigen'; };
        var played = !!(it.UserData && it.UserData.Played), fav = !!(it.UserData && it.UserData.IsFavorite);
        $('played').onclick = function () { var b = this; api('/Users/' + C.uid + '/PlayedItems/' + id, { method: played ? 'DELETE' : 'POST' }).then(function () { played = !played; b.textContent = played ? '✓ Gesehen' : 'Als gesehen markieren'; }); };
        $('fav').onclick = function () { var b = this; api('/Users/' + C.uid + '/FavoriteItems/' + id, { method: fav ? 'DELETE' : 'POST' }).then(function () { fav = !fav; b.textContent = fav ? '★ Favorit' : '☆ Favorit'; }); };
        if (!ch) { $('prev').disabled = $('next').disabled = true; return; }
        var url = sid ? { series: sid, sort: 'Series', limit: 200 } : { channel: ch, sort: 'Published', desc: 'false', limit: 200 };
        api('/Youfin/Videos?' + q(url)).then(function (p) {
          var i = p.Items.map(function (x) { return x.ItemId; }).indexOf(id), pv = p.Items[i - 1], nx = p.Items[i + 1];
          $('prev').disabled = !pv; $('next').disabled = !nx;
          if (pv) $('prev').onclick = function () { location.hash = '#/video/' + pv.ItemId; };
          if (nx) $('next').onclick = function () { location.hash = '#/video/' + nx.ItemId; };
          var rel = p.Items.filter(function (x) { return x.ItemId !== id; }).slice(0, 12);
          if (rel.length || sid) { $('rel').innerHTML = rel.map(function (v) { return card(v); }).join(''); return; }
          // nothing else from this channel: suggest other standalone videos instead of an empty column
          $('relh').textContent = 'Weitere Einzelvideos';
          return api('/Youfin/Videos?single=true&sort=Added&limit=13').then(function (o) {
            $('rel').innerHTML = o.Items.filter(function (x) { return x.ItemId !== id; }).slice(0, 12).map(function (v) { return card(v); }).join('');
          });
        });
      }
    }
  };
  function chanList(title, favs) {
    main.innerHTML = '<div class="row"><h2>' + title + '</h2><input id="cs" placeholder="Kanal suchen" class="btn"></div><div class="chips"><a href="#/channels" class="' + (favs ? '' : 'on') + '">Alle</a><a href="#/mychannels" class="' + (favs ? 'on' : '') + '">Favoriten</a></div><div id="list"></div>';
    var t;
    function go() { paged($('list'), function (s) { return api('/Youfin/Channels?' + q({ search: $('cs').value, favorites: favs ? 'true' : '', startIndex: s, limit: PAGE })); }, chanCard, 'chans', favs ? 'Noch keine Favoriten. Auf einer Kanalseite mit ☆ markieren.' : null); }
    $('cs').oninput = function () { clearTimeout(t); t = setTimeout(go, 250); };
    go();
  }
  function route() {
    setSel(false);
    var h = (location.hash || '#/').slice(2).split('/'), name = h[0] || 'home', arg = h[1] ? decodeURIComponent(h[1]) : null;
    if (name === 'playlists') name = 'collections'; else if (name === 'playlist') name = 'collection';
    document.querySelectorAll('nav a').forEach(function (a) { a.classList.toggle('on', a.dataset.r.split(',').indexOf(name) >= 0); });
    document.body.classList.remove('shownav');
    if (!C) { main.innerHTML = '<div class="empty">Bitte zuerst in <a href="/web/"><u>Jellyfin</u></a> anmelden.</div>'; return; }
    (routes[name] || routes.home)(arg);
    window.scrollTo(0, 0);
  }
  $('sf').onsubmit = function (e) { e.preventDefault(); var t = $('sq').value.trim(); if (t) location.hash = '#/search/' + encodeURIComponent(t); };
  $('menu').onclick = function () { document.body.classList.toggle(window.innerWidth > 900 ? 'nonav' : 'shownav'); };
  window.addEventListener('hashchange', route);
  if (C) { api('/Users/' + C.uid).then(function (u) { isAdmin = !!(u.Policy && u.Policy.IsAdministrator); }).catch(function () { }).then(route); } else { route(); }
})();
