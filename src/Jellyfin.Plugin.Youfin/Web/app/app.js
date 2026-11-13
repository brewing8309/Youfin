(function () {
  'use strict';
  var PAGE = 36;
  var main = document.getElementById('main');
  var view = localStorage.getItem('youfin.view') || 'grid';
  var isAdmin = false;
  var vc = {};            // video cache: id -> dto (for the ⋯ menu)
  var sel = {};           // selected video ids
  var selMode = false;
  // Jellyfin may be served under a base path (reverse proxy), e.g. /jellyfin
  var BASE = location.pathname.indexOf('/Youfin/Web') >= 0 ? location.pathname.slice(0, location.pathname.indexOf('/Youfin/Web')) : '';

  // ---------- i18n ----------
  // Texts live in i18n/<code>.json (English is the fallback). To add a language see docs/TRANSLATING.md.
  var LANG = 'en', dict = {}, fallback = {}, langs = [{ code: 'en', name: 'English' }];
  function jget(url) { return fetch(BASE + url).then(function (r) { return r.ok ? r.json() : null; }).catch(function () { return null; }); }
  function pickLang() {
    var codes = langs.map(function (l) { return l.code.toLowerCase(); }), saved = localStorage.getItem('youfin.lang');
    if (saved && codes.indexOf(saved.toLowerCase()) >= 0) return saved;
    var prefs = navigator.languages && navigator.languages.length ? navigator.languages : [navigator.language || 'en'];
    for (var i = 0; i < prefs.length; i++) {
      var lc = String(prefs[i]).toLowerCase();
      if (codes.indexOf(lc) >= 0) return langs[codes.indexOf(lc)].code;
      var base = lc.split('-')[0];
      if (codes.indexOf(base) >= 0) return langs[codes.indexOf(base)].code;
    }
    return 'en';
  }
  function loadI18n() {
    return jget('/Youfin/Web/languages.json').then(function (l) {
      if (Array.isArray(l) && l.length) langs = l;
      LANG = pickLang();
      return Promise.all([jget('/Youfin/Web/i18n/en.json'), LANG === 'en' ? null : jget('/Youfin/Web/i18n/' + LANG + '.json')]);
    }).then(function (r) { fallback = r[0] || {}; dict = r[1] || {}; });
  }
  // tr(key, params) returns the text; with params.n the plural forms key_one, key_other, ... are used (Intl.PluralRules)
  function tr(key, p) {
    var v, cat = p && p.n != null ? new Intl.PluralRules(LANG).select(p.n) : null;
    [dict, fallback].forEach(function (d) {
      if (v !== undefined) return;
      if (cat && d[key + '_' + cat] !== undefined) v = d[key + '_' + cat];
      else if (cat && d[key + '_other'] !== undefined) v = d[key + '_other'];
      else if (d[key] !== undefined) v = d[key];
    });
    if (v === undefined) return key;
    return v.replace(/\{(\w+)\}/g, function (m, k) { return p && p[k] != null ? p[k] : m; });
  }
  function applyStatic() {
    document.documentElement.lang = LANG;
    document.querySelectorAll('[data-i18n]').forEach(function (e) { e.textContent = tr(e.getAttribute('data-i18n')); });
    ['placeholder', 'aria-label', 'title'].forEach(function (a) {
      document.querySelectorAll('[data-i18n-' + a + ']').forEach(function (e) { e.setAttribute(a, tr(e.getAttribute('data-i18n-' + a))); });
    });
    var s = document.getElementById('lang');
    s.innerHTML = langs.map(function (l) { return '<option value="' + l.code + '">' + l.name + '</option>'; }).join('');
    s.value = LANG;
    s.onchange = function () { localStorage.setItem('youfin.lang', s.value); location.reload(); };
    document.getElementById('tojf').href = BASE + '/web/';
  }

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
    return fetch(BASE + path, opts).then(function (r) {
      if (!r.ok) throw new Error(r.status === 403 ? tr('err.forbidden') : r.status === 409 ? tr('err.conflict') : tr('err.http', { code: r.status }));
      return r.status === 204 ? null : r.json();
    });
  }
  function send(method, path, body) {
    return api(path, { method: method, headers: { 'Content-Type': 'application/json' }, body: body == null ? undefined : JSON.stringify(body) });
  }
  function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
  function img(id, w) { return BASE + '/Items/' + id + '/Images/Primary?maxWidth=' + w + '&api_key=' + encodeURIComponent(C.token); }
  function dur(x) { if (!x) return ''; var s = Math.round(x / 1e7), h = Math.floor(s / 3600), m = Math.floor(s % 3600 / 60), sec = s % 60; return (h ? h + ':' + String(m).padStart(2, '0') : m) + ':' + String(sec).padStart(2, '0'); }
  function date(d) { return d ? new Date(d).toLocaleDateString(LANG) : ''; }
  function q(o) { return Object.keys(o).filter(function (k) { return o[k] != null && o[k] !== ''; }).map(function (k) { return k + '=' + encodeURIComponent(o[k]); }).join('&'); }
  function $(id) { return document.getElementById(id); }
  function fail(e) { main.innerHTML = '<div class="empty">' + esc(tr('err.page', { msg: e.message })) + '</div>'; }
  function toast(msg) { var el = $('toast'); el.textContent = msg; el.hidden = false; clearTimeout(toast.t); toast.t = setTimeout(function () { el.hidden = true; }, 2500); }
  function ids(o) { return Object.keys(o); }
  function playUrl(id) { return BASE + '/web/#/details?id=' + id; }
  function btn(label, fn, cls) { var b = document.createElement('button'); b.className = 'btn' + (cls ? ' ' + cls : ''); b.textContent = label; b.onclick = fn; return b; }

  function dialog(title, items) {
    return new Promise(function (resolve) {
      var m = document.createElement('div'); m.className = 'modal';
      m.innerHTML = '<div class="box"><h3>' + esc(title) + '</h3>' + items.map(function (it, i) { return '<button class="btn" data-i="' + i + '">' + esc(it.label) + '</button>'; }).join('') + '<button class="btn" data-i="-1">' + esc(tr('dlg.cancel')) + '</button></div>';
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
      items.push({ label: tr('col.newItem'), id: null });
      return dialog(tr('col.addTitle'), items).then(function (o) {
        if (!o) return;
        if (o.id) return guard(send('POST', '/Youfin/Collections/' + o.id + '/Videos', { VideoIds: list }).then(function () { toast(tr('col.added')); }));
        var n = prompt(tr('col.newPrompt')); if (!n) return;
        return guard(send('POST', '/Youfin/Collections', { Name: n }).then(function (c) { return send('POST', '/Youfin/Collections/' + c.Id + '/Videos', { VideoIds: list }); }).then(function () { toast(tr('col.createdToast')); }));
      });
    }).catch(function (e) { toast(e.message); });
  }
  function addToSeries(list) {
    api('/Youfin/Series?limit=200').then(function (p) {
      if (!p.Items.length) { toast(tr('ser.noneYet')); return; }
      return dialog(tr('ser.addTitle'), p.Items.map(function (s) { return { label: s.Name + ' (' + s.VideoCount + ')', id: s.Id }; })).then(function (o) {
        if (o) guard(send('POST', '/Youfin/Series/' + o.id + '/Videos', { VideoIds: list }).then(function () { done(tr('ser.added')); }));
      });
    }).catch(function (e) { toast(e.message); });
  }
  function newSeries(list) {
    var n = prompt(tr('ser.newPrompt')); if (!n) return;
    guard(send('POST', '/Youfin/Series', { Name: n, VideoIds: list }).then(function (s) { location.hash = '#/serie/' + s.Id; }));
  }
  function videoMenu(list) {
    var multi = list.length > 1, v = vc[list[0]] || {};
    var o = [
      { label: tr('menu.watchlater'), run: function () { guard(send('POST', '/Youfin/Collections/watchlater/Videos', { VideoIds: list }).then(function () { toast(tr('menu.watchlaterDone')); })); } },
      { label: tr('menu.addCol'), run: addToCollection }
    ];
    if (isAdmin) {
      o.push({ label: tr('menu.addSer'), run: addToSeries }, { label: tr('menu.newSer'), run: newSeries });
      if (multi || v.SeriesId) o.push({ label: multi ? tr('menu.detachMany') : tr('menu.detachOne'), run: function (l) { guard(send('POST', '/Youfin/Videos/Single', { VideoIds: l }).then(function () { done(tr('menu.detached')); })); } });
    }
    dialog(multi ? tr('menu.videos', { n: list.length }) : (v.Title || tr('menu.video')), o).then(function (x) { if (x) x.run(list); });
  }

  // ---------- selection ----------
  function setSel(on) {
    selMode = on; if (!on) sel = {};
    document.body.classList.toggle('selmode', on); updateSel();
    document.querySelectorAll('.card.picked').forEach(function (c) { c.classList.remove('picked'); });
  }
  function updateSel() { $('selbar').hidden = !selMode; $('selcount').textContent = tr('sel.count', { n: ids(sel).length }); }
  main.addEventListener('click', function (e) {
    var k = e.target.closest('[data-menu]');
    if (k) { e.preventDefault(); videoMenu([k.dataset.menu]); return; }
    if (selMode) {
      var c = e.target.closest('.card');
      if (c && c.dataset.id) { e.preventDefault(); if (sel[c.dataset.id]) { delete sel[c.dataset.id]; } else { sel[c.dataset.id] = 1; } c.classList.toggle('picked', !!sel[c.dataset.id]); updateSel(); }
    }
  });
  $('selact').onclick = function () { var l = ids(sel); if (l.length) videoMenu(l); else toast(tr('sel.none')); };
  $('seldone').onclick = function () { setSel(false); };
  function selButton(parent) { var b = btn(tr('sel.button'), function () { setSel(!selMode); }); parent.appendChild(b); return b; }

  // ---------- cards ----------
  function card(v, badge) {
    vc[v.ItemId] = v;
    var pct = v.PositionTicks && v.RuntimeTicks ? Math.min(100, v.PositionTicks / v.RuntimeTicks * 100) : 0;
    return '<div class="card' + (sel[v.ItemId] ? ' picked' : '') + '" data-id="' + v.ItemId + '"><a class="thumb" href="#/video/' + v.ItemId + '"><img loading="lazy" src="' + img(v.ItemId, 480) + '" alt="">' +
      (v.RuntimeTicks ? '<span class="dur">' + dur(v.RuntimeTicks) + '</span>' : '') + (pct ? '<span class="bar" style="width:' + pct + '%"></span>' : '') + '</a>' +
      '<button class="kebab" data-menu="' + v.ItemId + '" aria-label="' + esc(tr('card.actions')) + '">⋯</button>' +
      '<div><h3>' + (badge ? '<span class="badge">' + esc(badge) + '</span>' : '') + '<a href="#/video/' + v.ItemId + '">' + esc(v.Title) + '</a></h3><div class="meta"><a href="#/channel/' + encodeURIComponent(v.ChannelKey) + '">' + esc(v.ChannelName) + '</a>' +
      (v.SeriesId && v.SeriesName !== v.ChannelName ? ' · <a href="#/serie/' + v.SeriesId + '">' + esc(v.SeriesName) + '</a>' : '') + '<br>' + date(v.Published) + '</div></div></div>';
  }
  function chanCard(c) {
    return '<a class="chan" href="#/channel/' + encodeURIComponent(c.Key) + '"><div class="av">' + (c.SampleVideoId ? '<img loading="lazy" src="' + img(c.SampleVideoId, 240) + '" alt="">' : '▶') + '</div><h3>' + esc(c.Name) + '</h3><div class="meta">' + esc(tr('card.videos', { n: c.VideoCount })) + (c.IsFavorite ? ' <span class="star">★</span>' : '') + '</div></a>';
  }
  function serCard(s) {
    return '<div class="card"><a class="thumb" href="#/serie/' + s.Id + '">' + (s.FirstVideoId ? '<img loading="lazy" src="' + img(s.FirstVideoId, 480) + '" alt="">' : '') + '<span class="dur">' + esc(tr('card.episodes', { n: s.VideoCount })) + '</span></a><div><h3><a href="#/serie/' + s.Id + '">' + esc(s.Name) + '</a></h3><div class="meta">' + esc(s.AutoChannelKey ? tr('card.autoRule') : tr('card.manual')) + '</div></div></div>';
  }

  // Paged grid with infinite scroll; load(start) -> Promise<{Items,Total}>
  function paged(el, load, render, cls, emptyMsg) {
    var start = 0;
    el.innerHTML = '<div class="grid ' + (cls || (view === 'list' ? 'list' : '')) + '"></div><a class="btn more" style="display:none">' + esc(tr('more.load')) + '</a>';
    var grid = el.firstChild, more = el.lastChild;
    function next() {
      load(start).then(function (p) {
        if (!start && !p.Items.length) { el.innerHTML = '<div class="empty">' + esc(emptyMsg || tr('empty.default')) + '</div>'; return; }
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
    main.innerHTML = '<div class="row"><h2>' + esc(title) + '</h2><span id="tools"></span></div><div id="list"></div>';
    var tools = $('tools'), list = $('list');
    if (opts.sortable) { tools.insertAdjacentHTML('beforeend', '<select id="sort"><option value="Added">' + esc(tr('sort.added')) + '</option><option value="Published">' + esc(tr('sort.published')) + '</option><option value="Title">' + esc(tr('sort.title')) + '</option></select> '); }
    tools.insertAdjacentHTML('beforeend', '<select id="view"><option value="grid">' + esc(tr('view.grid')) + '</option><option value="list">' + esc(tr('view.list')) + '</option></select> ');
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
    h.innerHTML = '<div class="row"><h2>' + esc(title) + '</h2>' + (link ? '<a class="btn" href="' + link + '">' + esc(tr('row.all')) + '</a>' : '') + '</div><div></div>';
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
      row(main, tr('home.resume'), '#/resume', '/Youfin/Videos?filter=resume&limit=8');
      row(main, tr('home.nextUp'), '#/series', '/Youfin/NextUp?limit=8');
      row(main, tr('home.newSingles'), '#/single', '/Youfin/Videos?single=true&sort=Added&limit=8');
      row(main, tr('home.watchlater'), '#/watchlater', '/Youfin/Videos?collection=watchlater&limit=8');
      row(main, tr('home.myChannels'), '#/mychannels', '/Youfin/Channels?favorites=true&limit=12', function (l) { return l.map(function (c) { return chanCard(c); }).join(''); });
      // random unplayed single videos, sampled client-side from one page
      row(main, tr('home.archive'), null, '/Youfin/Videos?single=true&sort=Added&limit=200', function (items) {
        return items.filter(function (v) { return !v.Played; }).sort(function () { return Math.random() - .5; }).slice(0, 8).map(function (v) { return card(v); }).join('');
      });
      row(main, tr('home.recentAdded'), '#/new', '/Youfin/Videos?sort=Added&limit=8');
      row(main, tr('home.recentWatched'), '#/history', '/Youfin/Videos?filter=recent&limit=8');
    },
    series: function () {
      main.innerHTML = '<div class="row"><h2>' + esc(tr('ser.title')) + '</h2><span id="tools"></span></div><div class="sub">' + esc(tr('ser.sub')) + '</div><div id="list"></div>';
      if (isAdmin) $('tools').appendChild(btn(tr('ser.newBtn'), function () { newSeries([]); }, 'pri'));
      paged($('list'), function (s) { return api('/Youfin/Series?' + q({ startIndex: s, limit: PAGE })); }, serCard, null, tr('ser.empty'));
    },
    serie: function (id) {
      Promise.all([api('/Youfin/Series/' + id), api('/Youfin/Videos?' + q({ series: id, sort: 'Series', limit: 200 }))]).then(function (r) {
        var s = r[0], items = r[1].Items, played = items.filter(function (v) { return v.Played; }).length;
        var next = items.filter(function (v) { return !v.Played; })[0];
        main.innerHTML = '<div class="row"><h2>' + esc(s.Name) + '</h2><span id="tools"></span></div><div class="sub">' + esc(tr('card.episodes', { n: s.VideoCount }) + ' · ' + tr('ser.watched', { n: played }) +
          (s.AutoChannelKey ? ' · ' + tr('card.autoRule') : '')) + '</div><div class="actions" id="sact"></div><div id="list"></div>';
        var act = $('sact');
        if (next) act.insertAdjacentHTML('beforeend', '<a class="btn pri" href="#/video/' + next.ItemId + '">' + esc(played ? tr('ser.watchNext') : tr('ser.watchFirst')) + '</a>');
        if (isAdmin) {
          var sortNames = { published: tr('ser.sort.published'), manual: tr('ser.sort.manual'), title: tr('ser.sort.title') };
          act.appendChild(btn(tr('ser.rename'), function () { var n = prompt(tr('ser.renamePrompt'), s.Name); if (n) guard(send('PUT', '/Youfin/Series/' + id, { Name: n }).then(function () { done(tr('ser.saved')); })); }));
          act.appendChild(btn(tr('ser.sortLabel', { mode: sortNames[s.Sort] || s.Sort }), function () {
            dialog(tr('ser.sortTitle'), [{ label: tr('ser.sortOpt.published'), v: 'published' }, { label: tr('ser.sortOpt.title'), v: 'title' }, { label: tr('ser.sortOpt.manual'), v: 'manual' }]).then(function (o) { if (o) guard(send('PUT', '/Youfin/Series/' + id, { Sort: o.v }).then(function () { done(tr('ser.saved')); })); });
          }));
          if (s.AutoChannelKey) act.appendChild(btn(tr('ser.removeRule'), function () { guard(send('PUT', '/Youfin/Series/' + id, { AutoChannelKey: '' }).then(function () { done(tr('ser.ruleRemoved')); })); }));
          if (items.length > 1 && s.VideoCount <= 200) act.appendChild(btn(tr('ser.reorder'), function () {
            reorderable($('list'), items.slice(), function (l) { guard(send('PUT', '/Youfin/Series/' + id + '/Order', { VideoIds: l })); });
            toast(tr('ser.reorderHint'));
          }));
          act.appendChild(btn(tr('ser.delete'), function () { if (confirm(tr('ser.deleteConfirm', { name: s.Name }))) guard(send('DELETE', '/Youfin/Series/' + id).then(function () { location.hash = '#/series'; })); }));
        }
        selButton($('tools'));
        $('list').innerHTML = '<div class="grid list">' + items.map(function (v, i) { return card(v, '#' + (i + 1)); }).join('') + '</div>' + (s.VideoCount > items.length ? '<div class="empty">' + esc(tr('ser.first200')) + '</div>' : '');
      }).catch(fail);
    },
    single: function () { videosPage(tr('single.title'), { single: 'true', sortable: true, empty: tr('single.empty') }); },
    channels: function () { chanList(tr('ch.all'), false); },
    mychannels: function () { chanList(tr('ch.favs'), true); },
    channel: function (key) {
      main.innerHTML = '<div class="chead"><div class="av" id="cav">▶</div><div><h2 id="cname" style="margin:0"></h2><div class="meta" id="ccount"></div></div></div><div class="actions" id="cact"></div><div id="cv"></div>';
      var act = $('cact'), ek = encodeURIComponent(key);
      var playAll = document.createElement('a'); playAll.className = 'btn pri'; playAll.textContent = tr('ch.playAll'); act.appendChild(playAll);
      var favBtn = btn(tr('ch.fav'), function () { }); act.appendChild(favBtn);
      if (isAdmin) {
        act.appendChild(btn(tr('ch.makeSeries'), function () { if (confirm(tr('ch.makeSeriesConfirm'))) guard(send('POST', '/Youfin/Series', { AutoChannelKey: key }).then(function (s) { location.hash = '#/serie/' + s.Id; })); }));
        act.appendChild(btn(tr('ch.rename'), function () { var n = prompt(tr('ch.renamePrompt'), $('cname').textContent); if (n !== null) guard(send('PUT', '/Youfin/Channels/' + ek + '/Name', { Name: n }).then(function () { done(tr('ser.saved')); })); }));
        act.appendChild(btn(tr('ch.merge'), function () { api('/Youfin/Channels?limit=200').then(function (p) {
          var l = p.Items.filter(function (c) { return c.Key !== key; });
          return dialog(tr('ch.mergeTitle'), l.map(function (c) { return { label: c.Name + ' (' + c.VideoCount + ')', key: c.Key }; })).then(function (o) { if (o) guard(send('POST', '/Youfin/Channels/' + ek + '/Merge', { Into: o.key }).then(function () { location.hash = '#/channel/' + encodeURIComponent(o.key); })); }); }); }));
      }
      api('/Youfin/Videos?' + q({ channel: key, sort: 'Published', desc: 'false', limit: 1 })).then(function (p) {
        if (!p.Items.length) return;
        $('cname').textContent = p.Items[0].ChannelName; $('ccount').textContent = tr('card.videos', { n: p.Total });
        $('cav').innerHTML = '<img src="' + img(p.Items[0].ItemId, 240) + '" alt="">';
        playAll.href = '#/video/' + p.Items[0].ItemId;
      });
      api('/Youfin/Channels?favorites=true&limit=200').then(function (p) {
        var fav = p.Items.some(function (c) { return c.Key === key; });
        function paint() { favBtn.textContent = fav ? tr('ch.faved') : tr('ch.fav'); }
        paint(); favBtn.onclick = function () { api('/Youfin/Channels/' + ek + '/Favorite', { method: fav ? 'DELETE' : 'POST' }).then(function () { fav = !fav; paint(); }); };
      });
      var cv = $('cv');
      cv.innerHTML = '<div class="row"><span id="tools"></span><select id="sort"><option value="Published">' + esc(tr('ch.sortNewest')) + '</option><option value="PublishedAsc">' + esc(tr('ch.sortOldest')) + '</option><option value="Title">' + esc(tr('sort.title')) + '</option></select></div><div id="cl"></div>';
      selButton($('tools'));
      var s1 = $('sort');
      function go() {
        var asc = s1.value === 'PublishedAsc', s = asc ? 'Published' : s1.value;
        paged($('cl'), function (st) { return api('/Youfin/Videos?' + q({ channel: key, sort: s, desc: s === 'Title' ? 'false' : String(!asc), startIndex: st, limit: PAGE })); }, card);
      }
      s1.onchange = go; go();
    },
    videos: function () { videosPage(tr('page.all'), { sortable: true }); },
    new: function () { videosPage(tr('page.new'), { sort: 'Added' }); },
    resume: function () { videosPage(tr('page.resume'), { filter: 'resume' }); },
    history: function () { videosPage(tr('page.history'), { filter: 'recent' }); },
    favorites: function () { videosPage(tr('page.favorites'), { filter: 'favorites' }); },
    watchlater: function () { routes.collection('watchlater'); },
    search: function (text) {
      main.innerHTML = '<h2>' + esc(tr('search.title', { q: text })) + '</h2><h3>' + esc(tr('search.series')) + '</h3><div id="ss"></div><h3>' + esc(tr('search.channels')) + '</h3><div id="sc"></div><h3>' + esc(tr('search.videos')) + '</h3><div id="sv"></div>';
      api('/Youfin/Series?' + q({ search: text, limit: 12 })).then(function (p) { $('ss').innerHTML = p.Items.length ? '<div class="grid">' + p.Items.map(function (s) { return serCard(s); }).join('') + '</div>' : '<div class="empty">' + esc(tr('search.noSeries')) + '</div>'; });
      api('/Youfin/Channels?' + q({ search: text, limit: 12 })).then(function (p) { $('sc').innerHTML = p.Items.length ? '<div class="grid chans">' + p.Items.map(function (c) { return chanCard(c); }).join('') + '</div>' : '<div class="empty">' + esc(tr('search.noChannels')) + '</div>'; });
      paged($('sv'), function (s) { return api('/Youfin/Videos?' + q({ q: text, sort: 'Published', startIndex: s, limit: PAGE })); }, card);
    },
    categories: function () {
      main.innerHTML = '<h2>' + esc(tr('cat.title')) + '</h2><div class="tags" id="cats"></div>';
      api('/Youfin/Categories').then(function (l) { $('cats').innerHTML = l.length ? l.map(function (c) { return '<a href="#/category/' + encodeURIComponent(c.Name) + '">' + esc(c.Name) + ' (' + c.VideoCount + ')</a>'; }).join('') : '<div class="empty">' + esc(tr('cat.empty')) + '</div>'; });
    },
    category: function (n) { videosPage(tr('cat.page', { name: n }), { category: n, sortable: true }); },
    collections: function () {
      main.innerHTML = '<div class="row"><h2>' + esc(tr('col.title')) + '</h2><span id="tools"></span></div><div class="sub">' + esc(tr('col.sub')) + '</div><div class="tags" id="cols"></div>';
      $('tools').appendChild(btn(tr('col.newBtn'), function () { var n = prompt(tr('col.namePrompt')); if (n) guard(send('POST', '/Youfin/Collections', { Name: n }).then(function (c) { location.hash = '#/collection/' + c.Id; })); }, 'pri'));
      api('/Youfin/Collections').then(function (l) { $('cols').innerHTML = l.map(function (c) { return '<a href="#/collection/' + c.Id + '">' + (c.Kind === 'watchLater' ? '⏱ ' + esc(tr('col.watchLaterName')) : esc(c.Name)) + ' (' + c.VideoCount + ')</a>'; }).join(''); });
    },
    collection: function (id) {
      Promise.all([api('/Youfin/Collections'), api('/Youfin/Videos?' + q({ collection: id, limit: 200 }))]).then(function (r) {
        var c = r[0].filter(function (x) { return x.Id === id; })[0] || { Name: '', Kind: 'watchLater' }, items = r[1].Items, later = c.Kind === 'watchLater';
        main.innerHTML = '<div class="row"><h2>' + esc(later ? tr('col.watchLaterName') : c.Name) + '</h2><span id="tools"></span></div><div class="sub">' + esc(tr('col.count', { n: r[1].Total }) + (later ? '' : tr('col.reorderable'))) + '</div><div class="actions" id="cact"></div><div id="list"></div>';
        var act = $('cact');
        if (items.length) act.insertAdjacentHTML('beforeend', '<a class="btn pri" href="#/video/' + items[0].ItemId + '">' + esc(tr('col.playFirst')) + '</a>');
        if (!later) {
          act.appendChild(btn(tr('col.rename'), function () { var n = prompt(tr('ser.renamePrompt'), c.Name); if (n) guard(send('PUT', '/Youfin/Collections/' + id, { Name: n }).then(function () { done(tr('ser.saved')); })); }));
          act.appendChild(btn(tr('col.delete'), function () { if (confirm(tr('col.deleteConfirm'))) guard(send('DELETE', '/Youfin/Collections/' + id).then(function () { location.hash = '#/collections'; })); }));
        }
        if (!items.length) { $('list').innerHTML = '<div class="empty">' + esc(tr('col.empty')) + '</div>'; return; }
        reorderable($('list'), items, function (l) { guard(send('PUT', '/Youfin/Collections/' + id + '/Order', { VideoIds: l })); }, function (vid) { guard(api('/Youfin/Collections/' + id + '/Videos/' + vid, { method: 'DELETE' })); });
      }).catch(fail);
    },
    video: function (id) {
      Promise.all([api('/Users/' + C.uid + '/Items/' + id), api('/Youfin/Videos/' + id).catch(function () { return null; })]).then(function (r) { render(r[0], r[1]); }).catch(fail);
      function render(it, me) {
        if (me) vc[id] = me;
        var ch = me ? me.ChannelKey : null, play = playUrl(id), sid = me && me.SeriesId;
        var played = !!(it.UserData && it.UserData.Played), fav = !!(it.UserData && it.UserData.IsFavorite);
        var meta = [me ? '<a href="#/channel/' + encodeURIComponent(ch) + '">' + esc(me.ChannelName) + '</a>' : '',
          sid ? esc(tr('vid.seriesLabel', { link: '\u0000' })).replace('\u0000', '<a href="#/serie/' + sid + '">' + esc(me.SeriesName) + '</a>') : (me ? esc(tr('vid.standalone')) : ''), esc(date(it.PremiereDate)), esc(dur(it.RunTimeTicks))].filter(Boolean).join(' · ');
        main.innerHTML = '<div class="detail"><div><a class="hero" href="' + play + '"><img src="' + img(id, 1280) + '" alt=""><span class="play">▶</span></a>' +
          '<h2 style="margin-top:14px">' + esc(it.Name) + '</h2><div class="meta">' + meta + '</div>' +
          '<div class="actions"><a class="btn pri" href="' + play + '">' + esc(tr('vid.play')) + '</a><button class="btn" id="prev">' + esc(tr('vid.prev')) + '</button><button class="btn" id="next">' + esc(tr('vid.next')) + '</button>' +
          '<button class="btn" id="played"></button><button class="btn" id="fav"></button><button class="btn" id="more" data-menu="' + id + '">' + esc(tr('vid.more')) + '</button></div>' +
          '<div class="desc clamp" id="desc">' + esc(it.Overview || tr('vid.noDesc')) + '</div><button class="btn" id="tog" style="margin-top:8px">' + esc(tr('vid.showMore')) + '</button></div>' +
          '<div><h2 id="relh">' + esc(sid ? tr('vid.relSeries') : tr('vid.relChannel')) + '</h2><div class="grid list" id="rel"></div></div></div>';
        $('tog').onclick = function () { var d = $('desc'); d.classList.toggle('clamp'); this.textContent = d.classList.contains('clamp') ? tr('vid.showMore') : tr('vid.showLess'); };
        function paintPlayed() { $('played').textContent = played ? tr('vid.played') : tr('vid.markPlayed'); }
        function paintFav() { $('fav').textContent = fav ? tr('ch.faved') : tr('ch.fav'); }
        paintPlayed(); paintFav();
        $('played').onclick = function () { api('/Users/' + C.uid + '/PlayedItems/' + id, { method: played ? 'DELETE' : 'POST' }).then(function () { played = !played; paintPlayed(); }); };
        $('fav').onclick = function () { api('/Users/' + C.uid + '/FavoriteItems/' + id, { method: fav ? 'DELETE' : 'POST' }).then(function () { fav = !fav; paintFav(); }); };
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
          $('relh').textContent = tr('vid.relSingles');
          return api('/Youfin/Videos?single=true&sort=Added&limit=13').then(function (o) {
            $('rel').innerHTML = o.Items.filter(function (x) { return x.ItemId !== id; }).slice(0, 12).map(function (v) { return card(v); }).join('');
          });
        });
      }
    }
  };
  function chanList(title, favs) {
    main.innerHTML = '<div class="row"><h2>' + esc(title) + '</h2><input id="cs" placeholder="' + esc(tr('ch.search')) + '" class="btn"></div><div class="chips"><a href="#/channels" class="' + (favs ? '' : 'on') + '">' + esc(tr('ch.chipAll')) + '</a><a href="#/mychannels" class="' + (favs ? 'on' : '') + '">' + esc(tr('ch.favs')) + '</a></div><div id="list"></div>';
    var timer;
    function go() { paged($('list'), function (s) { return api('/Youfin/Channels?' + q({ search: $('cs').value, favorites: favs ? 'true' : '', startIndex: s, limit: PAGE })); }, chanCard, 'chans', favs ? tr('ch.noFavs') : null); }
    $('cs').oninput = function () { clearTimeout(timer); timer = setTimeout(go, 250); };
    go();
  }
  function route() {
    setSel(false);
    document.querySelectorAll('.modal').forEach(function (m) { m.remove(); });   // close open dialogs on navigation
    var h = (location.hash || '#/').slice(2).split('/'), name = h[0] || 'home', arg = h[1] ? decodeURIComponent(h[1]) : null;
    if (name === 'playlists') name = 'collections'; else if (name === 'playlist') name = 'collection';
    document.querySelectorAll('nav a').forEach(function (a) { a.classList.toggle('on', (a.dataset.r || '').split(',').indexOf(name) >= 0); });
    document.body.classList.remove('shownav');
    if (!C) { main.innerHTML = '<div class="empty">' + esc(tr('login.required', { link: '\u0000' })).replace('\u0000', '<a href="' + BASE + '/web/"><u>Jellyfin</u></a>') + '</div>'; return; }
    (routes[name] || routes.home)(arg);
    window.scrollTo(0, 0);
  }
  // Appropriate Legal Notices (GPL-3.0 section 5d / NOTICE): copyright, AI statement, link to the original project, license, no warranty
  function showAbout() {
    var m = document.createElement('div'); m.className = 'modal';
    m.innerHTML = '<div class="box"><h3>' + esc(tr('about.title')) + '</h3><p>' + esc(tr('about.copyright')) + '</p><p>' + esc(tr('about.created')) + '</p>' +
      '<p><a href="https://github.com/brewing8309/Youfin" target="_blank" rel="noopener">' + esc(tr('about.source')) + '</a></p><p>' + esc(tr('about.license')) + '</p><p>' + esc(tr('about.warranty')) + '</p>' +
      '<button class="btn" id="aboutclose">' + esc(tr('about.close')) + '</button></div>';
    m.onclick = function (e) { if (e.target === m || e.target.id === 'aboutclose') m.remove(); };
    document.body.appendChild(m);
  }
  $('aboutlink').onclick = function (e) { e.preventDefault(); showAbout(); };
  $('sf').onsubmit = function (e) { e.preventDefault(); var text = $('sq').value.trim(); if (text) location.hash = '#/search/' + encodeURIComponent(text); };
  $('menu').onclick = function () { document.body.classList.toggle(window.innerWidth > 900 ? 'nonav' : 'shownav'); };
  window.addEventListener('hashchange', route);
  loadI18n().then(function () {
    applyStatic();
    if (C) { return api('/Users/' + C.uid).then(function (u) { isAdmin = !!(u.Policy && u.Policy.IsAdministrator); }).catch(function () { }); }
  }).then(route);
})();
