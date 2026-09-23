'use strict';
const $ = id => document.getElementById(id);
const incoming = location.hash.slice(1);
if (incoming) { sessionStorage.setItem('vizhi-token', incoming); history.replaceState(null, '', '/'); }
const token = sessionStorage.getItem('vizhi-token') || '';
let run, visible = [], selectedId, saving = false;
const resultOf = c => run.results[c.id]?.status || 'NOT_TESTED';
const message = text => { $('message').textContent = text; };
async function api(path, payload) {
  const response = await fetch(path, {method: payload ? 'POST' : 'GET', headers: {'X-Vizhi-Token': token, ...(payload ? {'Content-Type': 'application/json'} : {})}, body: payload ? JSON.stringify(payload) : undefined});
  const data = await response.json();
  if (!response.ok) throw new Error(data.error || response.statusText);
  return data;
}
function option(value, label) { const o = document.createElement('option'); o.value = value; o.textContent = label; return o; }
function badge(text, cls = '') { const e = document.createElement('span'); e.className = 'badge ' + cls; e.textContent = text; return e; }
function selected() { return visible.find(c => c.id === selectedId); }
function draftKey(c) { return 'vizhi-draft:' + run.id + ':' + c.id; }
function rememberNotes() {
  const c = selected(); if (c) localStorage.setItem(draftKey(c), JSON.stringify({notes: $('notes').value, evidence: $('evidence').value}));
}
function renderHeader() {
  $('runState').textContent = `Overall evidence: ${run.summary.state}`; $('runState').className = run.stale ? 'STALE' : '';
  $('build').textContent = `${run.fingerprint.branch} · ${run.fingerprint.commit.slice(0, 8)}${run.fingerprint.dirty ? ' + working changes' : ''} · run ${run.id}`;
  const counts = Object.fromEntries(['PASS','FAIL','BLOCKED','NOT_TESTED'].map(s => [s, run.cases.filter(c => resultOf(c) === s).length]));
  $('totals').textContent = `Hardware / live app: ${counts.PASS} passed · ${counts.FAIL} failed · ${counts.BLOCKED} blocked · ${counts.NOT_TESTED} not tested`;
  const software = run.summary.software;
  $('automationState').textContent = run.stale ? 'STALE EVIDENCE' : run.automation?.status || 'Individual stages';
  $('automationState').className = 'badge ' + (run.stale ? 'STALE' : run.automation?.status || '');
  const tests = run.checks['command-coverage']?.counts;
  $('softwareTotals').textContent = `${software.variants.PASS}/${software.variantTotal} command/mode variants passed · ${software.coveredCases}/${run.cases.length} checklist cases linked to passing dispatch tests` + (tests ? ` · C# suite: ${tests.Passed} passed, ${tests.Failed} failed, ${tests.NotExecuted} skipped` : '');
  $('coverageRows').replaceChildren(...software.groups.map(g => {
    const row = document.createElement('tr');
    for (const value of [g.profile, ...['ChatGPT','Codex'].map(m => `${g.modes[m].covered}/${g.modes[m].total}`), g.modes.ChatGPT.unmapped + g.modes.Codex.unmapped, `${g.liveRecorded}/${g.total}`]) {
      const cell = document.createElement('td'); cell.textContent = value; row.append(cell);
    }
    return row;
  }));
  $('checks').replaceChildren(...Object.entries(run.checks).map(([name, c]) => badge(`${name}: ${c.status}${c.reason ? ' · ' + c.reason : ''}`, c.status)));
  $('notice').textContent = run.notice;
  if (run.stale) message('This build or configuration changed. Results are retained as stale. Start a new run to record fresh acceptance.');
}
function filter() {
  visible = run.cases.filter(c => ($('profile').value === 'all' || c.profile === $('profile').value) && ($('mode').value === 'all' || c.mode === $('mode').value) && ($('scope').value === 'full' || c.smoke) && ($('resultFilter').value === 'all' || resultOf(c) === $('resultFilter').value));
  if (!visible.some(c => c.id === selectedId)) selectedId = visible[0]?.id;
  render();
}
function render() {
  renderHeader();
  $('listCount').textContent = `${visible.length} ${visible.length === 1 ? 'case' : 'cases'}`;
  $('caseList').replaceChildren(...visible.map(c => {
    const b = document.createElement('button'); b.className = 'caseItem' + (c.id === selectedId ? ' selected' : '');
    const dot = document.createElement('span'); dot.className = 'dot ' + resultOf(c); dot.textContent = {PASS:'✓',FAIL:'×',BLOCKED:'!',NOT_TESTED:'○'}[resultOf(c)];
    b.append(dot, document.createTextNode(c.title)); const info = document.createElement('small');
    info.textContent = `${c.mode} · ${c.page}${c.position ? ' · key ' + c.position : ''}`; b.append(info);
    b.onclick = () => { rememberNotes(); selectedId = c.id; render(); }; return b;
  }));
  const c = selected(); $('case').hidden = !c; $('empty').hidden = !!c; if (!c) return;
  localStorage.setItem('vizhi-position:' + run.id, c.id);
  $('caseLocation').textContent = `${c.profile} / ${c.page} / ${c.mode}`;
  $('caseTitle').textContent = c.title; $('caseNumber').textContent = `${visible.indexOf(c) + 1} / ${visible.length}`;
  const auto = run.automatedCases[c.automatedCase]?.status;
  $('badges').replaceChildren(badge(`Hardware: ${resultOf(c).replaceAll('_', ' ')}`, resultOf(c)), badge(auto ? `Simulated dispatch: ${auto}` : c.automatedCase ? 'Simulation not run' : 'No exact dispatch mapping'), ...(c.capability === 'unsupported' ? [badge('Feature unsupported · check disabled behavior', 'unsupported')] : []));
  for (const field of ['prepare','action','expectedApp','expectedKey']) $(field).textContent = c[field];
  const profile = run.profiles.find(p => p.name === c.profile), page = profile?.pages.find(p => p.name === c.page);
  $('keypad').replaceChildren(...Array.from({length:9}, (_, i) => {
    const key = document.createElement('div'); key.className = 'key' + (c.position === i + 1 ? ' current' : '');
    const binding = page?.keys.find(k => k.position === i + 1);
    const other = binding && run.cases.find(x => x.profile === c.profile && x.page === c.page && x.position === i + 1 && x.mode === c.mode);
    key.textContent = other?.title || `${i + 1}`; key.setAttribute('aria-label', `Key ${i+1}${c.position === i + 1 ? ', press this key' : ''}`); return key;
  }));
  $('mapCaption').textContent = c.position ? `Key ${c.position} · row ${Math.floor((c.position - 1)/3)+1}, column ${(c.position - 1)%3+1}` : 'Use the assigned action; no default position.';
  $('workflow').hidden = !c.configuredWorkflow; $('prompt').textContent = c.configuredWorkflow?.prompt || '';
  const saved = run.results[c.id] || {}, draft = JSON.parse(localStorage.getItem(draftKey(c)) || 'null');
  $('notes').value = draft?.notes ?? saved.notes ?? ''; $('evidence').value = draft?.evidence ?? saved.evidence ?? '';
  $('lastSaved').textContent = saved.when ? `Saved ${new Date(saved.when).toLocaleString()} · retesting keeps previous results in JSON history.` : 'No hardware result recorded. Notes stay in this browser until you save a result or Save notes.';
  $('selectionProgress').textContent = `${visible.filter(x => resultOf(x)==='PASS').length} / ${visible.length} passed in this selection`;
  $('previous').disabled = visible.indexOf(c) === 0; $('next').disabled = visible.indexOf(c) === visible.length - 1;
  for (const id of ['pass','fail','blocked','saveNotes','resetResult','saveOperator']) $(id).disabled = run.stale || saving;
}
async function record(status, advance) {
  const c = selected(); if (!c || saving) return; rememberNotes();
  saving = true;
  for (const id of ['pass','fail','blocked','saveNotes','resetResult']) $(id).disabled = true;
  try {
    const result = {status, notes: $('notes').value, evidence: $('evidence').value};
    const response = await api('/api/result', {revision: run.revision, caseId:c.id, ...result});
    run.revision = response.revision; run.results[c.id] = {...result, when:new Date().toISOString()};
    localStorage.removeItem(draftKey(c)); message(`Saved ${status.replaceAll('_',' ')} — ${c.title}`);
    if (advance) selectedId = visible[visible.indexOf(c)+1]?.id || c.id;
    // Refresh server summary; the owner result never changes simulated evidence.
    run = await api('/api/run');
  } catch (e) { message(e.message); }
  finally { saving = false; filter(); }
}
for (const id of ['profile','mode','scope','resultFilter']) $(id).onchange = () => { rememberNotes(); filter(); };
$('notes').oninput = rememberNotes; $('evidence').oninput = rememberNotes;
$('pass').onclick = () => record('PASS', true); $('fail').onclick = () => record('FAIL', true); $('blocked').onclick = () => record('BLOCKED', true);
$('saveNotes').onclick = () => record(resultOf(selected()), false); $('resetResult').onclick = () => record('NOT_TESTED', false);
$('previous').onclick = () => { rememberNotes(); selectedId = visible[visible.indexOf(selected())-1]?.id; render(); };
$('next').onclick = () => { rememberNotes(); selectedId = visible[visible.indexOf(selected())+1]?.id; render(); };
$('saveOperator').onclick = async () => {
  try { const result = await api('/api/operator', {revision:run.revision, operator:$('operator').value, appVersion:$('appVersion').value, device:$('device').value}); run.revision=result.revision; message('Test details saved.'); }
  catch(e) { message(e.message); }
};
for (const b of document.querySelectorAll('[data-export]')) b.onclick = async () => {
  try { const response = await fetch('/export/'+b.dataset.export,{headers:{'X-Vizhi-Token':token}}); if (!response.ok) throw new Error('Export failed'); const url=URL.createObjectURL(await response.blob()); const a=document.createElement('a'); a.href=url; a.download=run.id+'-'+b.dataset.export; a.click(); setTimeout(()=>URL.revokeObjectURL(url),1000); }
  catch(e) { message(e.message); }
};
(async () => {
  try {
    run = await api('/api/run');
    $('profile').replaceChildren(...[...new Set(run.cases.map(c=>c.profile))].map(p=>option(p,p)), option('all','All profiles & actions'));
    if (run.profiles.some(p => p.name === 'Vizhi Flow 2')) $('profile').value = 'Vizhi Flow 2';
    for(const id of ['operator','appVersion','device']) $(id).value=run[id];
    selectedId=localStorage.getItem('vizhi-position:'+run.id);
    const resume=run.cases.find(c=>c.id===selectedId);
    if(resume) { $('profile').value=resume.profile; $('mode').value=resume.mode; if(!resume.smoke) $('scope').value='full'; }
    filter();
  } catch(e) { message(e.message); $('runState').textContent='Could not load run'; }
})();
