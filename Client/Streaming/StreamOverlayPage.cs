namespace AionDPS.Streaming;

/// <summary>
/// The files of the stream widgets: one HTML a widget, one style, one script, no libraries and nothing loaded from elsewhere (see the server's
/// Content-Security-Policy). Redone 6 November 2026 (the owner: "fix obs overlay, its kinda shit rn"): three widgets, each its own link for its own browser source -
/// the meter (as over the game: class colours and icons, the bar's grey and green parts with their "+" figures, the boss line, ten lines that slide into their
/// new places), the character card (portrait or class emblem in its class colour, level, server, legion, Item Level, Combat Power, and this stream: how long,
/// the bosses killed, the best DPS, the Combat Power gained) and a wide boss bar (its health, when it dies, its enrage). Link options: ?scale=1.25 to draw it
/// larger, ?rows=5 for fewer lines, ?style=solid (no see-through) or ?style=clear (no panel), ?always=1 to keep it on screen between fights, ?demo=1 for made-up
/// numbers while setting it up.
/// </summary>
public static class StreamOverlayPage
{
    /// <summary>The widgets the server serves, by the last part of their link (the bare link is the meter).</summary>
    public static readonly IReadOnlyList<string> Widgets = new[] { "meter", "card", "boss" };

    public static string HtmlFor(string widget)
    {
        string body = widget switch
        {
            "card" => """
                <div id="card" class="panel hide"><div class="c-art"><img id="c-art" alt=""></div>
                <div class="c-face"><div class="c-ring"></div><img id="c-emblem" class="emblem" alt=""><img id="c-portrait" class="portrait" alt="" hidden></div>
                <div class="c-body"><div class="c-kicker"><span class="live-dot"></span><span id="c-live"></span></div>
                <div id="c-name" class="c-name"></div><div id="c-sub" class="c-sub"></div>
                <div class="c-stats"><div class="stat"><small>ITEM LEVEL</small><b id="c-il" class="num"></b></div>
                <div class="stat"><small>COMBAT POWER</small><b id="c-cp" class="num"></b><em id="c-cpg" class="num"></em></div>
                <div class="stat" id="c-bosses-box"><small>BOSSES</small><b id="c-bosses" class="num"></b></div>
                <div class="stat" id="c-best-box"><small>BEST DPS</small><b id="c-best" class="num"></b><em id="c-bestboss" class="where"></em></div></div></div></div>
                """,
            "boss" => """
                <div id="boss" class="panel hide"><div class="bw-top"><span id="bw-name"></span><span class="grow"></span><span id="bw-kill" class="num"></span><span id="bw-rage" class="num"></span></div>
                <div class="bw-bar"><i id="bw-fill"></i><span id="bw-pct" class="num"></span></div><div id="bw-hp" class="bw-hp num"></div></div>
                """,
            _ => """
                <div id="meter" class="panel hide"><div class="m-head"><span id="m-mode" class="chip"></span><span id="m-time" class="num"></span><span class="grow"></span><span id="m-total" class="num"></span><span id="m-unit"></span></div>
                <div id="m-boss" hidden><div class="b-line"><span id="mb-name"></span><span id="mb-hp" class="num"></span><span class="grow"></span><span id="mb-kill" class="num"></span><span id="mb-rage" class="num"></span></div><div class="b-bar"><i id="mb-fill"></i></div></div>
                <div id="m-cols" class="cols"></div><div id="m-rows"></div><div id="m-empty" class="m-empty" hidden>Waiting for a fight</div></div>
                """,
        };
        return "<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\"><title>Atreia App</title>\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n"
            + "<link rel=\"stylesheet\" href=\"overlay.css\"></head>\n<body data-widget=\"" + (Widgets.Contains(widget) ? widget : "meter") + "\">\n" + body
            + "\n<script src=\"overlay.js\"></script></body></html>\n";
    }

    /// <summary>The meter's page (the bare link).</summary>
    public static string Html => HtmlFor("meter");

    public const string Style = """
        :root { color-scheme: dark; --gold: #e6c068; --text: #f3f5f8; --muted: #a6b0bd; --subtle: #6f7a88; --panel: rgba(12, 15, 22, .80); --line: rgba(255, 255, 255, .09); --c: #9fb3c8; }
        html, body { margin: 0; background: transparent; color: var(--text); font-family: "Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif; overflow: hidden; -webkit-font-smoothing: antialiased; }
        body { padding: 8px; }
        .num { font-variant-numeric: tabular-nums; }
        .grow { flex: 1; }
        [hidden] { display: none !important; }
        .panel { position: relative; box-sizing: border-box; width: 100%; background: var(--panel); border: 1px solid var(--line); border-radius: 14px; box-shadow: 0 10px 30px rgba(0, 0, 0, .35);
                 transition: opacity .5s ease, transform .5s ease; }
        .panel.hide { opacity: 0; transform: translateY(8px); }
        body.s-solid .panel { background: #0d1017; }
        body.s-clear .panel { background: transparent; border-color: transparent; box-shadow: none; }
        .ok { color: #6cd48a; } .late { color: #ef6b6b; } .warn { color: #f0b44a; } .soon { animation: pulse 1s ease-in-out infinite; }
        @keyframes pulse { 50% { opacity: .45; } }

        /* the meter */
        #meter { padding: 10px 10px 8px; }
        .m-head { display: flex; align-items: center; gap: 9px; padding: 0 4px 9px; }
        .chip { font-size: 11px; font-weight: 800; letter-spacing: .08em; color: #12151c; background: var(--gold); border-radius: 6px; padding: 2px 7px 3px; }
        #m-time { font-size: 12.5px; color: var(--muted); font-weight: 600; }
        #m-total { font-size: 21px; font-weight: 800; color: var(--gold); line-height: 1; }
        #m-unit { font-size: 11px; font-weight: 700; letter-spacing: .06em; color: var(--muted); }
        #m-boss { margin: -2px 4px 9px; }
        .b-line { display: flex; align-items: baseline; gap: 8px; font-size: 12px; white-space: nowrap; }
        .b-line span { flex: none; }
        #mb-name { flex: 0 1 auto; min-width: 0; font-weight: 700; overflow: hidden; text-overflow: ellipsis; }
        #mb-hp { color: var(--muted); }
        #mb-kill, #mb-rage { font-weight: 700; }
        .b-bar { height: 4px; border-radius: 2px; background: rgba(255, 255, 255, .08); margin-top: 5px; overflow: hidden; }
        .b-bar i { display: block; height: 100%; width: 0; border-radius: 2px; transition: width .5s ease, background .5s; }
        .cols { display: flex; justify-content: flex-end; gap: 0; padding: 0 9px 4px; font-size: 10px; font-weight: 700; letter-spacing: .08em; color: var(--subtle); text-transform: uppercase; }
        .cols span { text-align: right; min-width: 48px; }
        .cols span.main { min-width: 58px; }
        #m-rows { position: relative; }
        .row { position: relative; display: flex; align-items: center; height: 29px; margin: 0 0 3px; padding: 0 9px 0 8px; border-radius: 8px; background: rgba(255, 255, 255, .035);
               overflow: hidden; transition: transform .5s cubic-bezier(.2, .8, .2, 1), opacity .4s; }
        .row::before { content: ""; position: absolute; left: 0; top: 0; bottom: 0; width: 3px; background: var(--c); z-index: 1; }
        .row .fill { position: absolute; left: 0; top: 0; bottom: 0; right: 0; }
        .row .fill i { position: absolute; top: 0; bottom: 0; left: 0; width: 0; transition: left .5s ease, width .5s ease; }
        .row .fill .given { background: rgba(180, 189, 200, .36); }
        .row .fill .heal { background: rgba(82, 210, 134, .42); }
        .row > :not(.fill) { position: relative; }
        .row .rank { width: 15px; font-size: 11px; font-weight: 700; color: var(--muted); text-align: center; margin-right: 5px; }
        .row .ic { width: 20px; height: 20px; margin-right: 7px; filter: drop-shadow(0 1px 1px rgba(0, 0, 0, .7)); }
        .row .name { flex: 1; min-width: 0; font-size: 13.5px; font-weight: 700; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; text-shadow: 0 1px 2px rgba(0, 0, 0, .85); }
        .row .plus { font-size: 11px; font-weight: 800; margin-left: 6px; white-space: nowrap; text-shadow: 0 1px 2px rgba(0, 0, 0, .85); }
        .row .plus .pg { color: #c6ccd5; } .row .plus .ph { color: #7be3a4; margin-left: 5px; }
        .row .cell { min-width: 48px; text-align: right; font-size: 12px; color: #d4dae2; text-shadow: 0 1px 2px rgba(0, 0, 0, .85); }
        .row .cell.main { min-width: 58px; font-size: 14.5px; font-weight: 800; color: #fff; }
        .row .sub { position: absolute; right: 9px; bottom: 1px; font-size: 9px; color: var(--muted); }
        .row.self { box-shadow: inset 0 0 0 1px rgba(230, 192, 104, .7); }
        .row.self .name { color: var(--gold); }
        .row.new { opacity: 0; }
        .m-empty { font-size: 12px; color: var(--muted); padding: 2px 6px 4px; }

        /* the character card */
        #card { display: flex; align-items: center; gap: 18px; padding: 16px 22px 16px 18px; overflow: hidden; min-height: 120px; }
        #card::before { content: ""; position: absolute; left: 0; top: 14px; bottom: 14px; width: 4px; border-radius: 0 3px 3px 0; background: var(--c); }
        .c-art { position: absolute; right: -36px; top: -40px; width: 210px; height: 210px; opacity: .10; pointer-events: none; }
        .c-art img { width: 100%; height: 100%; }
        .c-face { position: relative; width: 96px; height: 96px; flex: none; }
        .c-ring { position: absolute; inset: 0; border-radius: 50%; border: 3px solid var(--c); background: #10131a; }
        .c-face img.emblem { position: absolute; left: 18px; top: 18px; width: 60px; height: 60px; }
        .c-face img.portrait { position: absolute; left: 7px; top: 7px; width: 82px; height: 82px; border-radius: 50%; object-fit: cover; object-position: top; }
        .c-body { position: relative; min-width: 0; flex: 1; }
        .c-kicker { display: flex; align-items: center; gap: 7px; font-size: 11px; font-weight: 800; letter-spacing: .12em; color: var(--c); text-transform: uppercase; }
        .live-dot { width: 8px; height: 8px; border-radius: 50%; background: #ef5b5b; box-shadow: 0 0 8px #ef5b5b; animation: pulse 1.6s ease-in-out infinite; }
        .c-name { font-size: 31px; font-weight: 800; line-height: 1.1; margin-top: 3px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
        .c-sub { font-size: 13.5px; color: var(--muted); margin-top: 2px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
        .c-stats { display: flex; gap: 22px; margin-top: 11px; flex-wrap: wrap; }
        .stat small { display: block; font-size: 10px; font-weight: 800; letter-spacing: .1em; color: var(--subtle); }
        .stat b { font-size: 18px; font-weight: 800; }
        .stat em { font-style: normal; font-size: 12px; font-weight: 800; color: #6cd48a; margin-left: 6px; }
        .stat em.where { color: var(--muted); font-weight: 600; }

        /* the wide boss bar */
        #boss { padding: 11px 16px 12px; }
        .bw-top { display: flex; align-items: baseline; gap: 12px; }
        #bw-name { font-size: 17px; font-weight: 800; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
        #bw-kill, #bw-rage { font-size: 13.5px; font-weight: 800; }
        .bw-bar { position: relative; height: 18px; border-radius: 9px; background: rgba(255, 255, 255, .07); margin-top: 8px; overflow: hidden; }
        .bw-bar i { position: absolute; left: 0; top: 0; bottom: 0; width: 0; border-radius: 9px; transition: width .6s ease, background .6s; }
        #bw-pct { position: absolute; right: 9px; top: 0; line-height: 18px; font-size: 11.5px; font-weight: 800; text-shadow: 0 1px 2px rgba(0, 0, 0, .9); }
        .bw-hp { font-size: 12px; color: var(--muted); margin-top: 6px; }
        """;

    public const string Script = """
        (function () {
          var q = new URLSearchParams(location.search);
          var widget = document.body.getAttribute('data-widget') || 'meter';
          var scale = parseFloat(q.get('scale')) || 1;
          if (scale !== 1) { document.body.style.zoom = scale; }
          var style = q.get('style'); if (style) { document.body.classList.add('s-' + style); }
          var maxRows = parseInt(q.get('rows'), 10) || 0;
          var always = q.get('always') === '1';
          var demo = q.get('demo') === '1';
          function $(id) { return document.getElementById(id); }
          function rgba(hex, a) {
            var m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex || '');
            if (!m) { return 'rgba(159,179,200,' + a + ')'; }
            return 'rgba(' + parseInt(m[1], 16) + ',' + parseInt(m[2], 16) + ',' + parseInt(m[3], 16) + ',' + a + ')';
          }
          function text(el, value) { if (el && el.textContent !== value) { el.textContent = value; } }
          function hpColour(p) { return p > 50 ? 'linear-gradient(90deg,#3fb68b,#6cd48a)' : p > 20 ? 'linear-gradient(90deg,#d99a2b,#f0b44a)' : 'linear-gradient(90deg,#c94141,#ef6b6b)'; }
          function icon(cls) { return 'c/' + encodeURIComponent(cls || 'x') + '.png'; }
          function bossTimes(b, kill, rage) {
            text(kill, b.kill ? 'kill ' + b.kill : '');
            kill.className = 'num ' + (b.kill ? (b.killLate ? 'late' : 'ok') : '');
            text(rage, b.rage ? (b.rage === '0:00' ? 'enraged' : 'enrage ' + b.rage) : '');
            rage.className = 'num ' + (b.rageSoon ? 'late soon' : 'warn');
          }

          // ---- the meter
          var rows = {};
          function makeRow() {
            var row = document.createElement('div'); row.className = 'row new';
            row.innerHTML = '<div class="fill"><i class="own"></i><i class="given"></i><i class="heal"></i></div><span class="rank num"></span><img class="ic" alt="">'
              + '<span class="name"></span><span class="plus"><span class="pg"></span><span class="ph"></span></span>';
            row.querySelector('.ic').onerror = function () { this.style.visibility = 'hidden'; };
            return row;
          }
          function fillRow(row, r) {
            var c = r.color || '#9fb3c8';
            row.style.setProperty('--c', c);
            row.classList.toggle('self', !!r.self);
            var own = row.querySelector('.own'), given = row.querySelector('.given'), heal = row.querySelector('.heal');
            own.style.background = 'linear-gradient(90deg,' + rgba(c, .62) + ',' + rgba(c, .30) + ')';
            own.style.width = (r.bar * 100).toFixed(2) + '%';
            given.style.left = (r.bar * 100).toFixed(2) + '%'; given.style.width = ((r.given - r.bar) * 100).toFixed(2) + '%';
            heal.style.left = (r.given * 100).toFixed(2) + '%'; heal.style.width = ((r.heal - r.given) * 100).toFixed(2) + '%';
            text(row.querySelector('.rank'), r.rank ? String(r.rank) : '');
            var img = row.querySelector('.ic'); var src = icon(r.cls); if (img.getAttribute('src') !== src) { img.style.visibility = ''; img.setAttribute('src', src); }
            text(row.querySelector('.name'), r.name || '');
            text(row.querySelector('.pg'), r.plusGiven || ''); text(row.querySelector('.ph'), r.plusHeal || '');
            var cells = row.querySelectorAll('.cell');
            var want = r.cells || [];
            if (cells.length !== want.length) {
              Array.prototype.forEach.call(cells, function (e) { e.remove(); });
              want.forEach(function (_, i) { var e = document.createElement('span'); e.className = 'cell num' + (i === 0 ? ' main' : ''); row.appendChild(e); });
              cells = row.querySelectorAll('.cell');
            }
            want.forEach(function (v, i) { text(cells[i], v); });
            var sub = row.querySelector('.sub');
            if (r.sub) { if (!sub) { sub = document.createElement('span'); sub.className = 'sub'; row.appendChild(sub); } text(sub, r.sub); } else if (sub) { sub.remove(); }
          }
          function renderMeter(s) {
            var m = s.meter || {}, panel = $('meter'), list = $('m-rows');
            var lines = (m.rows || []).slice(0, maxRows || m.maxRows || 10);
            text($('m-mode'), m.mode || 'DPS'); text($('m-time'), m.time || ''); text($('m-total'), m.total || ''); text($('m-unit'), m.unit || '');
            var cols = $('m-cols'), heads = m.headers || [];
            if (cols.childElementCount !== heads.length || Array.prototype.some.call(cols.children, function (e, i) { return e.textContent !== heads[i]; })) {
              cols.textContent = '';
              heads.forEach(function (h, i) { var e = document.createElement('span'); e.className = i === 0 ? 'main' : ''; e.textContent = h; cols.appendChild(e); });
            }
            var b = s.boss;
            $('m-boss').hidden = !b;
            if (b) {
              text($('mb-name'), b.name); text($('mb-hp'), b.text || '');
              $('mb-fill').style.width = Math.max(0, Math.min(100, b.percent)) + '%'; $('mb-fill').style.background = hpColour(b.percent);
              bossTimes(b, $('mb-kill'), $('mb-rage'));
            }
            var before = {};
            Array.prototype.forEach.call(list.children, function (e) { before[e.dataset.k] = e.getBoundingClientRect().top; });
            var seen = {};
            lines.forEach(function (r) {
              var k = (r.name || '') + '|' + (r.cls || '');
              seen[k] = true;
              var row = rows[k]; if (!row) { row = rows[k] = makeRow(); row.dataset.k = k; }
              fillRow(row, r);
              list.appendChild(row);
            });
            Object.keys(rows).forEach(function (k) { if (!seen[k]) { rows[k].remove(); delete rows[k]; } });
            Array.prototype.forEach.call(list.children, function (row) {
              var top = before[row.dataset.k];
              if (top === undefined) { requestAnimationFrame(function () { row.classList.remove('new'); }); return; }
              var moved = top - row.getBoundingClientRect().top;
              if (Math.abs(moved) > 1) {
                row.style.transition = 'none'; row.style.transform = 'translateY(' + moved + 'px)';
                requestAnimationFrame(function () { requestAnimationFrame(function () { row.style.transition = ''; row.style.transform = ''; }); });
              }
            });
            $('m-empty').hidden = lines.length > 0 || !always;
            panel.classList.toggle('hide', lines.length === 0 && !always);
          }

          // ---- the character card
          function renderCard(s) {
            var me = s.me, panel = $('card');
            panel.classList.toggle('hide', !me);
            if (!me) { return; }
            var cls = me.cls || me.className || '';   // the app sends className
            panel.style.setProperty('--c', me.color || '#e6c068');
            $('c-art').src = icon(cls); $('c-emblem').src = icon(cls);
            var face = $('c-portrait');
            if (me.portrait) { if (face.getAttribute('src') !== me.portrait) { face.setAttribute('src', me.portrait); } face.hidden = false; $('c-emblem').hidden = true; }
            else { face.hidden = true; $('c-emblem').hidden = false; }
            text($('c-live'), me.live ? 'Live · ' + me.live : 'Playing now');
            text($('c-name'), me.name);
            var sub = [cls, me.level ? 'Level ' + me.level : '', me.server, me.legion ? '<' + me.legion + '>' : ''].filter(function (x) { return x; });
            text($('c-sub'), sub.join('  ·  '));
            text($('c-il'), me.itemLevel ? me.itemLevel.toLocaleString() : '–');
            text($('c-cp'), me.cp ? me.cp.toLocaleString() : '–');
            text($('c-cpg'), me.cpGain ? (me.cpGain > 0 ? '+' : '') + me.cpGain.toLocaleString() : '');
            $('c-cpg').className = 'num' + (me.cpGain < 0 ? ' late' : '');
            text($('c-bosses'), String(me.bosses || 0));
            $('c-best-box').hidden = !me.best;
            text($('c-best'), me.best || ''); text($('c-bestboss'), me.bestBoss || '');
          }

          // ---- the wide boss bar
          function renderBoss(s) {
            var b = s.boss, panel = $('boss');
            panel.classList.toggle('hide', !b);
            if (!b) { return; }
            text($('bw-name'), b.name);
            var p = Math.max(0, Math.min(100, b.percent));
            $('bw-fill').style.width = p + '%'; $('bw-fill').style.background = hpColour(p);
            text($('bw-pct'), p.toFixed(p < 10 ? 1 : 0) + '%');
            text($('bw-hp'), b.text || '');
            bossTimes(b, $('bw-kill'), $('bw-rage'));
          }

          function render(s) { if (widget === 'card') { renderCard(s); } else if (widget === 'boss') { renderBoss(s); } else { renderMeter(s); } }

          if (demo) {
            var t = 0;
            var people = [['Aerin', 'Gladiator', '#7ad0de', 9.1], ['Kaelo', 'Ranger', '#60b593', 8.4], ['Mirae', 'Sorcerer', '#aa7fe1', 7.9], ['Thorne', 'Templar', '#4ba4bd', 5.2],
                          ['Lunari', 'Chanter', '#dfa04e', 4.6], ['Vesh', 'Cleric', '#d9c777', 3.1]];
            var tick = function () {
              t += 1;
              var dps = people.map(function (p, i) { return p[3] * (1 + 0.08 * Math.sin(t / 2 + i * 1.7)); });
              var order = dps.map(function (v, i) { return i; }).sort(function (a, b) { return dps[b] - dps[a]; });
              var top = dps[order[0]] + 0.6, sum = dps.reduce(function (a, b) { return a + b; }, 0);
              var lines = order.map(function (i, n) {
                var p = people[i], given = p[1] === 'Chanter' ? 1.4 : p[1] === 'Cleric' ? 1.1 : p[1] === 'Gladiator' ? 0.2 : 0, heal = p[1] === 'Cleric' ? 2.6 : p[1] === 'Chanter' ? 0.9 : 0;
                var whole = Math.max(top, dps[order[0]] + 0.2);
                return { rank: n + 1, name: p[0], cls: p[1], color: p[2], self: i === 0, cells: [dps[i].toFixed(1) + 'K', (dps[i] / sum * 100).toFixed(1) + '%', (dps[i] * 111 / 1000).toFixed(2) + 'M'],
                         bar: dps[i] / whole, given: (dps[i] + given) / whole, heal: Math.min(1, (dps[i] + given + heal) / whole),
                         plusGiven: given ? '+' + (given >= 1 ? given.toFixed(1) + 'K' : (given * 1000).toFixed(0)) : '', plusHeal: heal ? '+' + heal.toFixed(1) + 'K' : '' };
              });
              var hp = Math.max(4, 100 - (t % 60) * 1.6);
              render({ meter: { mode: 'DPS', time: '1:' + String(t % 60).padStart(2, '0'), total: sum.toFixed(1) + 'K', unit: 'DPS', headers: ['DPS', '%', 'Total'], rows: lines, maxRows: 10 },
                       boss: { name: 'Transcendent Bakarma', percent: hp, text: (hp * 0.126).toFixed(2) + 'M / 12.6M', kill: '1:51', killLate: false, rage: '2:09', rageSoon: false },
                       me: { name: 'Aerin', cls: 'Gladiator', color: '#7ad0de', level: 45, server: 'Zikel', legion: 'Dawnbreakers', itemLevel: 2643, cp: 182730, portrait: null,
                             live: '1:35:20', bosses: 4, best: '94.1K', bestBoss: 'on Thamon', cpGain: 486 } });
            };
            tick(); setInterval(tick, 1600);
            return;
          }

          function connect() {
            var source = new EventSource('events');
            source.onmessage = function (e) { try { render(JSON.parse(e.data)); } catch (x) { } };
            source.onerror = function () { source.close(); setTimeout(connect, 2000); };
          }
          connect();
        })();
        """;
}
