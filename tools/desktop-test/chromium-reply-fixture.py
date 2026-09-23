#!/usr/bin/env python3
"""Test native reply copying or composer append in a disposable Chromium fixture.

Pass an Electron.app runtime and a fresh artifact directory. Only the fixed fixture bundle
and a named test pasteboard are used. The fixture blocks network requests.
After exit, retain fixture sources/results but remove the copied browser and its cache.
Set VIZHI_KEEP_CHROMIUM_FIXTURE=1 to retain the disposable app for further investigation.
"""
from pathlib import Path
import json, os, plistlib, signal, subprocess, time, sys, shutil, re

root = Path(__file__).resolve().parents[2]
assert len(sys.argv) in [3,4,5], 'Usage: chromium-reply-fixture.py /path/to/Electron.app /fresh/artifact/directory [variant] [built-helper]'
variant=sys.argv[3] if len(sys.argv)>=4 else 'normal'
changes_variants = ['codex-changes'+suffix for suffix in ['', '-already', '-no-ack', '-ambiguous', '-wrong-mode', '-file-only', '-mode-race', '-disabled', '-duplicate-panel', '-formatted', '-indian', '-compact']]
shortcut_variants = ['codex-changes-shortcut'+suffix for suffix in ['', '-already', '-no-ack', '-wrong-mode', '-dialog', '-disabled', '-ambiguous', '-mode-race', '-unconfigured']]
changes_variants += shortcut_variants
review_variants = ['codex-review-'+suffix for suffix in ['unavailable', 'preview-decoys', 'app-with-preview', 'empty', 'empty-already']]
assert variant in changes_variants + review_variants + ['normal','wrapped','wrapped-code-only','wrapped-split-row','wrapped-duplicate','no-ack','user-last','running','append','image','files','image-placeholder','image-placeholder-literal','codex-append','codex-image','codex-literal','preset','codex-preset','write','codex-write','spoken','codex-spoken','codex-copy-panel','codex-copy-two-chats','codex-copy-pending-panel','codex-copy-inner','codex-copy-panel-code-only','codex-copy-panel-running']
kind={'codex-append':'append','codex-image':'image-placeholder','codex-literal':'image-placeholder-literal','codex-preset':'preset','codex-write':'write','codex-spoken':'spoken'}.get(variant,variant)
surface_mode='Codex' if variant.startswith('codex-') else 'ChatGPT'
hint='Do anything' if surface_mode=='Codex' else 'Work with ChatGPT'
from fixture import adapter_placeholder_labels, review_changes_prompt
placeholder_args=[arg for label in adapter_placeholder_labels() for arg in ['--draft-placeholder',label]]
work = Path(sys.argv[2]).resolve()
work.mkdir(parents=True, exist_ok=True)
app = work / 'VizhiChromiumFixture.app'
runtime = Path(sys.argv[1]).resolve()
helper = work / 'VizhiAxBridge'
if len(sys.argv)==5: shutil.copy2(Path(sys.argv[4]).resolve(), helper)
else: subprocess.run(['bash',str(root/'tools/desktop/build.sh'),'--no-install','--output',str(helper)],cwd=root,check=True)
fixture_id = 'com.vizhi.desktop.testfixture'
args = [str(helper), '--app', fixture_id, '--copy-response', 'Copy response', '--copy-button', 'Copy', '--copy-completed', 'Copied', '--assistant-heading', 'ChatGPT said:', '--user-heading', 'You said:', '--response-action', 'Fork chat from here', '--response-action', 'Branch in new chat', '--response-action', 'More actions', '--response-action', 'Rate response', '--stop', 'Stop', '--test-pasteboard', 'com.vizhi.fixture.chromium-copy']
def ax(verb, *extra):
    p = subprocess.run([args[0],verb,*args[1:],*extra], capture_output=True, text=True, timeout=12)
    result=json.loads(p.stdout)
    (work / (verb+'.json')).write_text(json.dumps(result,indent=2)+'\n')
    return result
assert ax('frontmost').get('frontmost') is False, 'Fixture unexpectedly frontmost'
assert ax('status').get('error') == 'app-not-running', 'Fixture already running or AX unavailable'
assert runtime.exists(), 'Electron binary unavailable'
assert not app.exists(), 'Probe bundle already exists'
subprocess.run(['ditto',str(runtime),str(app)],check=True)
plist_path=app/'Contents/Info.plist'
p=plistlib.loads(plist_path.read_bytes()); p['CFBundleIdentifier']=fixture_id; p['CFBundleName']='Vizhi Chromium Fixture'
plist_path.write_bytes(plistlib.dumps(p))
source=app/'Contents/Resources/app'; source.mkdir()
(source/'package.json').write_text(json.dumps({'name':'vizhi-fixture','version':'1.0.0','main':'main.js'}))
(source/'preload.js').write_text("const {contextBridge,ipcRenderer}=require('electron'); contextBridge.exposeInMainWorld('fixture',{copy:(kind)=>ipcRenderer.invoke('fixture-copy',kind),paste:()=>ipcRenderer.invoke('fixture-paste'),image:()=>ipcRenderer.invoke('fixture-image'),report:(state)=>ipcRenderer.send('fixture-state',state)});")
(source/'main.js').write_text('''
const {app,BrowserWindow,ipcMain}=require('electron');
const fs=require('node:fs'), path=require('node:path'), cp=require('node:child_process');
const out=CONFIG;
let fixtureWindow;
app.setPath('userData',path.join(out,'user-data'));
app.commandLine.appendSwitch('force-renderer-accessibility');
app.whenReady().then(async()=>{
 const win=fixtureWindow=new BrowserWindow({width:850,height:750,title:'Vizhi isolated Chromium fixture',webPreferences:{preload:path.join(__dirname,'preload.js'),sandbox:true,contextIsolation:true,nodeIntegration:false}});
 win.webContents.session.webRequest.onBeforeRequest((details,callback)=>callback({cancel:!details.url.startsWith('file:')}));
 ipcMain.handle('fixture-paste',()=>cp.execFileSync(path.join(out,'FixtureClipboard'),['read'],{encoding:'utf8'}));
 ipcMain.handle('fixture-image',()=>cp.execFileSync(path.join(out,'FixtureClipboard'),['read-image'],{encoding:'utf8'}));
 ipcMain.on('fixture-state',(_e,state)=>fs.writeFileSync(path.join(out,'composer-state.json'),JSON.stringify(state)));
 ipcMain.handle('fixture-copy',(_e,kind)=>{
   if(!['old','user','code','reply'].includes(kind))throw Error('Invalid fixture copy');
   cp.execFileSync(path.join(out,'FixtureClipboard'),['write',kind]);
   fs.writeFileSync(path.join(out,'copied'),kind); return true;
 });
 await win.loadFile(path.join(__dirname,'fixture.html')); win.show(); win.focus();
 fs.writeFileSync(path.join(out,'ready'),JSON.stringify(process.versions));
});
app.on('window-all-closed',()=>app.quit());
'''.replace('CONFIG',json.dumps(str(work))))
(source/'fixture.html').write_text('''<!doctype html><html><body>
<aside><button>Recent</button><textarea aria-label="Sidebar note">Fixture note</textarea></aside>
<main><h4>ChatGPT said:</h4><p>Old fixture answer</p><div><button aria-label="Copy" onclick="copy(this,'old')">Old copy</button><button aria-label="Branch in new chat">Branch</button></div>
<h4>You said:</h4><p>Fixture prompt</p>
<h4>ChatGPT said:</h4><p>Full fixture response</p><div><button aria-label="Copy" onclick="copy(this,'code')">Code copy</button><pre>fixture code</pre></div>
<div style="display:flex;gap:4px"><button aria-label="Copy" onclick="copy(this,'reply')"><svg width="21" height="21" viewBox="0 0 21 21"><path d="M2 2H15V15H2Z"/></svg></button><button aria-label="Rate response"><svg width="21" height="21"><path d="M2 2H15V15H2Z"/></svg></button><button aria-label="Fork chat from here"><svg width="21" height="21"><path d="M2 2H15V15H2Z"/></svg></button></div>
<div role="textbox" contenteditable="true" aria-multiline="true" aria-label="Work with ChatGPT"></div></main>
<script>async function copy(button,kind){await window.fixture.copy(kind);button.setAttribute('aria-label','Copied');setTimeout(()=>button.setAttribute('aria-label','Copy'),2000)}</script>
</body></html>''')
html_path=source/'fixture.html'
html=html_path.read_text()
if variant.startswith('wrapped'):
 start=html.index('<div style="display:flex;gap:4px">')
 end=html.index('</div>',start)+6
 svg='<svg width="21" height="21"><path d="M2 2H15V15H2Z"/></svg>'
 def button(label,kind=None):
  click=' onclick="copy(this,\''+kind+'\')"' if kind else ''
  return '<button aria-label="'+label+'"'+click+'>'+svg+'</button>'
 copy=button('Copy','reply') if variant!='wrapped-code-only' else ''
 duplicate=button('Copy','old') if variant=='wrapped-duplicate' else ''
 row='<div role="group" aria-label="Fixture response toolbar" style="display:flex;align-items:center;gap:4px">'
 row+='<div role="group" aria-label="Copy wrapper">'+copy+'</div>'+duplicate+'<span>Now</span>'
 row+='<div role="group" aria-label="Rating wrapper">'+button('Rate response')+'</div>'
 branch_style=' style="margin-top:100px"' if variant=='wrapped-split-row' else ''
 row+='<div role="group" aria-label="Branch wrapper"'+branch_style+'>'+button('Fork chat from here')+'</div></div>'
 html=html[:start]+row+html[end:]
if variant=='no-ack':
 html=html.replace("await window.fixture.copy(kind);button.setAttribute('aria-label','Copied');setTimeout(()=>button.setAttribute('aria-label','Copy'),2000)","await window.fixture.copy(kind)")
 main=source/'main.js';main.write_text(main.read_text().replace("cp.execFileSync(path.join(out,'FixtureClipboard'),['write',kind]);",''))
if variant=='user-last': html=html.replace('<script>','<h4>You said:</h4><p>New fixture prompt</p><script>')
if variant=='running': html=html.replace('<script>','<button aria-label="Stop">Stop</button><script>')
if variant.startswith('codex-copy-'):
 import html as html_module
 panel='<h2>Diff preview</h2><pre>+ fixture change</pre><button aria-label="Copy response" onclick="parent.copy(this,\'code\')">Copy diff</button><button aria-label="Fork chat from here">Fork preview</button>'
 if variant=='codex-copy-two-chats': panel='<h4>ChatGPT said:</h4>'+panel
 if variant=='codex-copy-pending-panel': panel='<h4>You said:</h4>'+panel
 if variant=='codex-copy-panel-running': panel+='<button aria-label="Stop">Stop</button>'
 def iframe(content):
  return '<iframe title="Isolated fixture panel" srcdoc="'+html_module.escape(content,quote=True)+'"></iframe>'
 if variant=='codex-copy-inner':
  start=html.index('<main>');end=html.index('</main>')+len('</main>')
  html=html[:start]+panel.replace('parent.copy','copy')+iframe(html[start:end].replace('onclick="copy(', 'onclick="parent.copy('))+html[end:]
 else:
  if variant=='codex-copy-panel-code-only':
   start=html.index('<div style="display:flex;gap:4px">');end=html.index('</div>',start)+len('</div>')
   html=html[:start]+html[end:]
  html=html.replace('</main>', '</main>'+iframe(panel))
if kind in ['append','image','files','image-placeholder','image-placeholder-literal','preset','write','spoken']:
 html='''<!doctype html><html><body>
 <button aria-label="Mode: ChatGPT">ChatGPT</button>
 <div role="group" aria-label="Composer">
 <div id="editor" role="textbox" contenteditable="true" aria-multiline="true" aria-label="Message" style="white-space:pre-wrap;min-height:150px;border:1px solid grey"></div>
 <button id="send" aria-label="Send" disabled>Send</button>
 </div>
 <script>
 const editor=document.getElementById('editor'), send=document.getElementById('send'); let sent=[];
 function report(){send.disabled=!editor.innerText.trim();window.fixture.report({text:editor.innerText,sent});}
 editor.addEventListener('keydown',async e=>{if(e.metaKey&&e.code==='KeyV'){e.preventDefault();const text=await window.fixture.paste();document.execCommand('insertText',false,text);report();}});
 editor.addEventListener('input',report);new MutationObserver(report).observe(editor,{childList:true,subtree:true,characterData:true});
 send.onclick=()=>{sent.push(editor.innerText);editor.innerText='';report();};report();
 </script></body></html>'''
if kind in ['image','files','image-placeholder','image-placeholder-literal']:
 html=html.replace('let sent=[];', 'let sent=[], images=[];')
 html=html.replace('{text:editor.innerText,sent}', '{text:editor.innerText,sent,images}')
 html=html.replace('const text=await window.fixture.paste();', '''const files=JSON.parse(await window.fixture.image());
 if(files.length){for(const file of files){images.push(file);const chip=document.createElement('button');chip.textContent=file;chip.setAttribute('aria-label',file);editor.parentElement.appendChild(chip);}report();return;}
 const text=await window.fixture.paste();''')
if kind.startswith('image-placeholder') or surface_mode=='Codex':
 html=html.replace('aria-label="Message"', 'aria-label="'+hint+'"')
 html=html.replace('border:1px solid grey"></div>', 'border:1px solid grey"><p class="empty" data-placeholder="'+hint+'"><br></p></div>')
 html=html.replace('<script>', '<style>#editor p.empty:first-child:before { content: attr(data-placeholder); float: left; height: 0; color: grey; pointer-events: none; user-select: none; }</style><script>')
 html=html.replace('function report(){', "function report(){const p=editor.querySelector('p');if(p)p.classList.toggle('empty',!editor.innerText.trim());")
 if kind.startswith('image-placeholder'): html=html.replace('send.disabled=!editor.innerText.trim();', 'send.disabled=!editor.innerText.trim()&&!images.length;')
if surface_mode=='Codex': html=html.replace('aria-label="Mode: ChatGPT">ChatGPT', 'aria-label="Mode: Codex">Codex')
if kind=='spoken':
 # Model a plain-text paste handler rather than Chromium's default rich-text command,
 # which turns consecutive newlines into extra div/br spacing in innerText/AXValue.
 html=html.replace("document.execCommand('insertText',false,text)", """(()=>{
  const selection=window.getSelection(), range=selection.getRangeAt(0);
  range.deleteContents();const node=document.createTextNode(text);range.insertNode(node);
  range.setStartAfter(node);range.collapse(true);selection.removeAllRanges();selection.addRange(range);
  editor.dispatchEvent(new Event('input',{bubbles:true}));
 })()""")
if variant in changes_variants:
 mode='ChatGPT' if variant.endswith('-wrong-mode') else 'Codex'
 already=variant.endswith('-already') or variant.endswith('-duplicate-panel')
 panel='<section id="panel"><h2>Review</h2><button aria-label="Show files">Show files</button><pre>+ fixture change</pre></section>'
 opener='<button id="changes" aria-label="Changes +7 -2" onclick="openChanges()" '+('disabled' if variant.endswith('-disabled') else '')+'>Changes</button>'
 if variant.endswith(('-formatted','-indian','-compact')):
  counts=('+1,234','-56') if variant.endswith('-formatted') else ('+1,23,456','-7,890') if variant.endswith('-indian') else ('+1234','-56')
  # Like the app: accessible name derived from separate summary label and count spans.
  opener='<button id="changes" onclick="openChanges()"><span>Changes</span><span><span>'+counts[0]+'</span><span>'+counts[1]+'</span></span></button>'
 if variant.endswith('-file-only'): opener='<button aria-label="Toggle file diff" onclick="openChanges()">Wrong fallback</button>'
 if variant.endswith('-ambiguous'): opener+='<button aria-label="Changes +1 -0" onclick="openChanges()">Ambiguous</button>'
 html='<!doctype html><html><body><button id="mode" aria-label="Mode: '+mode+'">'+mode+'</button>'+opener
 html+='<div id="destination">'+(panel if already else '')+'</div>'
 if variant.endswith('-duplicate-panel'): html+='<button aria-label="Hide files">Second panel</button>'
 html+='<div role="textbox" contenteditable="true" aria-label="Message">Unsent fixture draft</div>'
 html+='<script>let clicks=0;function report(){window.fixture.report({clicks,open:!!document.getElementById("panel")});}function openChanges(){clicks++;'
 if variant.endswith('-mode-race'): html+='document.getElementById("mode").setAttribute("aria-label","Mode: ChatGPT");'
 if not variant.endswith('-no-ack'):
  # Intentionally toggle in the fixture: a repeated native press would close it.
  html+='document.getElementById("destination").innerHTML=document.getElementById("panel")?"":'+json.dumps(panel)+';'
 html+='report();}report();</script></body></html>'
if variant in shortcut_variants:
 mode='ChatGPT' if variant.endswith('-wrong-mode') else 'Codex'
 already=variant.endswith('-already')
 panel='<section id="panel"><h2>Review</h2><button aria-label="Show files">Show files</button><pre>+ fixture change</pre></section>'
 opener=''
 if variant.endswith('-disabled'): opener='<button disabled aria-label="Changes" onclick="wrongClick()">Disabled Changes</button>'
 if variant.endswith('-ambiguous'): opener='<button aria-label="Changes" onclick="wrongClick()">Changes</button><button aria-label="Changes +7 -2" onclick="wrongClick()">Changes</button>'
 html='<!doctype html><html><body><button id="mode" aria-label="Mode: '+mode+'">'+mode+'</button>'+opener
 html+='<div id="destination">'+(panel if already else '')+'</div>'
 html+='<div id="editor" role="textbox" contenteditable="true" aria-label="Message">Unsent fixture draft</div>'
 if variant.endswith('-dialog'): html+='<div role="dialog" aria-label="Fixture confirmation"><button>Cancel</button></div>'
 html+='<script>let clicks=0,shortcuts=0;function report(){window.fixture.report({clicks,shortcuts,open:!!document.getElementById("panel"),draft:document.getElementById("editor").innerText});}function wrongClick(){clicks++;report();}'
 html+='document.addEventListener("keydown",e=>{if(e.ctrlKey&&e.shiftKey&&!e.metaKey&&!e.altKey&&e.code==="KeyG"){e.preventDefault();shortcuts++;'
 if not variant.endswith('-no-ack'): html+='document.getElementById("destination").innerHTML='+json.dumps(panel)+';'
 if variant.endswith('-mode-race'): html+='document.getElementById("mode").setAttribute("aria-label","Mode: ChatGPT");'
 html+='report();}});report();</script></body></html>'
if variant in review_variants:
 import html as html_module
 already=variant.endswith('-already')
 supported=variant in ['codex-review-app-with-preview','codex-review-empty','codex-review-empty-already']
 panel='<section id="panel"><h2>Review</h2><button aria-label="Show files">Show files</button><p>No file changes yet</p></section>'
 opener='<button aria-label="Changes+0-0" onclick="openChanges()">Changes</button>' if supported else ''
 html='<!doctype html><html><body><button aria-label="Mode: Codex">Codex</button>'+opener
 html+='<aside><button>Review changes for portfolio<button aria-label="Pin chat">Pin</button></button></aside>'
 html+='<div id="destination">'+(panel if already else '')+'</div>'
 html+='<div id="editor" role="textbox" contenteditable="true" aria-label="Message">Unsent fixture draft</div>'
 if variant in ['codex-review-preview-decoys','codex-review-app-with-preview']:
  browser='<h1>Published portfolio</h1><button aria-label="Changes" onclick="parent.wrongClick()">Changes</button><button aria-label="Show files" onclick="parent.wrongClick()">Show files</button>'
  html+='<iframe title="Portfolio browser preview" srcdoc="'+html_module.escape(browser,quote=True)+'"></iframe>'
 html+='<script>let clicks=0,shortcuts=0,wrongClicks=0;function report(){window.fixture.report({clicks,shortcuts,wrongClicks,open:!!document.getElementById("panel"),draft:document.getElementById("editor").innerText});}function wrongClick(){wrongClicks++;report();}'
 html+='function openChanges(){clicks++;document.getElementById("destination").innerHTML='+json.dumps(panel)+';report();}'
 html+='document.addEventListener("keydown",e=>{if(e.ctrlKey&&e.shiftKey&&e.code==="KeyG"){e.preventDefault();shortcuts++;report();}});report();</script></body></html>'
html_path.write_text(html)
clipboard=work/'clipboard.swift'
clipboard.write_text('''import Cocoa
let board=NSPasteboard(name:NSPasteboard.Name("com.vizhi.fixture.chromium-copy"))
let args=CommandLine.arguments
if args.count==3 && args[1]=="write" && ["old","user","code","reply"].contains(args[2]) {
 board.clearContents(); let text=args[2]=="reply" ? "Full fixture response" : "Wrong fixture " + args[2]
 board.setString(text,forType:.string); board.setString("<p>"+text+"</p>",forType:.html)
} else if args.count==2 && args[1]=="seed" { board.clearContents(); board.setString("Unchanged fixture clipboard",forType:.string) }
else if args.count==2 && args[1]=="read" { FileHandle.standardOutput.write(Data((board.string(forType:.string) ?? "").utf8)) }
else if args.count==2 && args[1]=="read-image" {
 let urls=(board.readObjects(forClasses:[NSURL.self],options:[.urlReadingFileURLsOnly:true]) as? [URL]) ?? []
 FileHandle.standardOutput.write(try! JSONSerialization.data(withJSONObject:urls.map { $0.lastPathComponent }))
}
else {exit(2)}
''')
subprocess.run(['swiftc',str(clipboard),'-o',str(work/'FixtureClipboard')],check=True)
subprocess.run([str(work/'FixtureClipboard'),'seed'],check=True)
subprocess.run(['codesign','--force','--deep','--sign','-',str(app)],check=True,stdout=subprocess.DEVNULL)
for name in ['ready','copied']: (work/name).unlink(missing_ok=True)
log=(work/'app.log').open('w')
executable=app/'Contents/MacOS/Electron'
process=subprocess.Popen(['open','-n','-W',str(app)],stdout=log,stderr=log)
try:
 for _ in range(150):
  if (work/'ready').exists(): break
  time.sleep(.2)
 assert (work/'ready').exists(), 'Fixture not ready'
 assert ax('focus').get('ok')
 for _ in range(20):
  status=ax('status')
  if status.get('surface'): break
  time.sleep(.15)
 assert status.get('surface'), 'Fixture window did not expose its content'
 inspection=ax('inspect')
 if variant.startswith('codex-copy-'):
  assert any(n.get('text')=='Copy response' for n in inspection.get('nodes',[])), 'Auxiliary panel controls must be accessible'
 assert ax('focus').get('ok')
 for _ in range(20):
  frontmost=ax('frontmost').get('frontmost')
  if frontmost: break
  time.sleep(.15)
 assert frontmost is True, 'Foreground probe missed the focused fixture'
 if variant in review_variants:
  def state(): return json.loads((work/'composer-state.json').read_text())
  panel_args=['open-panel','--mode-prefix','Mode: ','--expect-mode','Codex','--panel-open','Changes','--panel-open','This branch','--panel-visible','Show files','--panel-visible','Hide files', '--panel-key-code','5','--panel-modifiers','control,shift']
  capability=ax('status','--mode-prefix','Mode: ','--panel-mode','Codex','--changes','Changes','--changes','This branch','--panel-visible','Show files','--panel-visible','Hide files')
  result=ax(*panel_args)
  if os.environ.get('VIZHI_EXPECT_OLD_REVIEW_ROUTE')=='1':
   assert variant=='codex-review-unavailable' and result.get('error')=='panel-shortcut-unconfirmed',result
   assert state()=={'clicks':0,'shortcuts':1,'wrongClicks':0,'open':False,'draft':'Unsent fixture draft'},state()
   report={'variant':variant,'status':'EXPECTED_FAILURE','result':result,'state':state()}
  else:
   success=variant in ['codex-review-app-with-preview','codex-review-empty','codex-review-empty-already']
   assert capability.get('changesPresent')==success,capability
   assert bool(result.get('ok'))==success,result
   assert state()['shortcuts']==0 and state()['wrongClicks']==0 and state()['draft']=='Unsent fixture draft',state()
   if success:
    assert result.get('opened') and state()['open'],(result,state())
    assert state()['clicks']==(0 if variant.endswith('-already') else 1),state()
    before=state();repeat=ax(*panel_args);assert repeat.get('ok') and repeat.get('alreadyOpen'),repeat
    assert state()==before,state()
   else:
    assert result.get('error')=='panel-not-available' and state()['clicks']==0 and not state()['open'],(result,state())
   assert ax('context-clipboard').get('text')=='Unchanged fixture clipboard'
   report={'variant':variant,'status':'PASS','result':result,'changesPresent':capability['changesPresent'],'state':state(),'repeatLeavesPanelOpen':success,
    'limitation':'Disposable fixture only; real app and physical keypad acceptance remain owner-run.'}
  (work/'fixture-result.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2));sys.exit(0)
 if variant in shortcut_variants:
  def state(): return json.loads((work/'composer-state.json').read_text())
  panel_args=['open-panel','--mode-prefix','Mode: ','--expect-mode','Codex','--panel-open','Changes','--panel-open','This branch','--panel-visible','Show files','--panel-visible','Hide files']
  if not variant.endswith('-unconfigured'): panel_args+=['--panel-key-code','5','--panel-modifiers','control,shift']
  result=ax(*panel_args)
  if os.environ.get('VIZHI_EXPECT_OLD_SHORTCUT_REFUSAL')=='1':
   assert variant=='codex-changes-shortcut' and result.get('error')=='panel-opener-missing',result
   assert state()=={'clicks':0,'shortcuts':0,'open':False,'draft':'Unsent fixture draft'},state()
   report={'variant':variant,'status':'EXPECTED_REFUSAL','result':result,'state':state()}
  else:
   success=variant=='codex-changes-shortcut-already'
   assert bool(result.get('ok'))==success,result
   expected_shortcuts=0
   assert state()['clicks']==0 and state()['shortcuts']==expected_shortcuts and state()['draft']=='Unsent fixture draft',state()
   if success:
    assert result.get('opened') and state()['open'],(result,state())
    if expected_shortcuts: assert result.get('method')=='shortcut',result
    before=state();repeat=ax(*panel_args);assert repeat.get('ok') and repeat.get('alreadyOpen'),repeat
    assert state()==before,state()
   else:
    expected={'wrong-mode':'mode-changed','dialog':'panel-obstructed','ambiguous':'panel-opener-multiple'}
    suffix=variant[len('codex-changes-shortcut-'):];assert result.get('error')==expected.get(suffix,'panel-not-available'),result
   assert ax('context-clipboard').get('text')=='Unchanged fixture clipboard'
   report={'variant':variant,'status':'PASS','result':result,'state':state(),'repeatLeavesPanelOpen':success,
    'limitation':'Shortcut events target only the disposable fixture. Real app/hardware acceptance remains owner-run.'}
  (work/'fixture-result.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2));sys.exit(0)
 if variant in changes_variants:
  def state(): return json.loads((work/'composer-state.json').read_text())
  ax('inspect') # Synthetic controls only; retain the browser's actual computed accessible label.
  panel_args=['open-panel','--mode-prefix','Mode: ','--expect-mode','Codex','--panel-open','Changes','--panel-open','This branch','--panel-visible','Show files','--panel-visible','Hide files']
  result=ax(*panel_args)
  success=variant in ['codex-changes','codex-changes-already','codex-changes-formatted','codex-changes-indian','codex-changes-compact']
  if os.environ.get('VIZHI_EXPECT_OLD_PANEL_REFUSAL') == '1':
   assert variant in ['codex-changes-formatted','codex-changes-indian','codex-changes-compact']
   assert result.get('error')=='panel-opener-unavailable' and state()=={'clicks':0,'open':False},(result,state())
   report={'variant':variant,'status':'EXPECTED_REFUSAL','result':result,'state':state()}
   (work/'fixture-result.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2));sys.exit(0)
  assert bool(result.get('ok'))==success,result
  if success:
   assert result.get('opened') is True and state()['open'], (result,state())
   before=state()
   repeat=ax(*panel_args);assert repeat.get('ok') and repeat.get('alreadyOpen') is True,repeat
   assert state()==before and state()['clicks']==(0 if variant.endswith('-already') else 1),state()
  else:
   expected={'codex-changes-no-ack':'panel-unconfirmed','codex-changes-mode-race':'mode-changed',
    'codex-changes-wrong-mode':'mode-changed','codex-changes-duplicate-panel':'panel-ambiguous',
    'codex-changes-ambiguous':'panel-opener-multiple','codex-changes-disabled':'panel-not-available'}.get(variant,'panel-not-available')
   assert result.get('error')==expected,result
   assert state()['clicks']==(1 if variant.endswith('-no-ack') or variant.endswith('-mode-race') else 0),state()
  assert ax('context-clipboard').get('text')=='Unchanged fixture clipboard'
  report={'variant':variant,'status':'PASS','result':result,'state':state(),'repeatLeavesPanelOpen':success,
   'limitation':'Isolated Chromium fixture; physical keypad and real app acceptance remain owner-run.'}
  (work/'fixture-result.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2));sys.exit(0)
 if kind=='spoken':
  examples=root/'src/Products/VizhiDesktop/package/examples'
  recipes=json.loads((examples/('desktop-workflows.json' if surface_mode=='Codex' else 'desktop-chatgpt-workflows.json')).read_text())
  if surface_mode=='Codex': recipes+=json.loads((examples/'desktop-workflow-extras.json').read_text())
  recipes=[r for r in recipes if r.get('input')=='voice']
  scoped=['--mode-prefix','Mode: ','--expect-mode',surface_mode,'--composer-send-label','Send',*placeholder_args]
  def state(): return json.loads((work/'composer-state.json').read_text())
  brief='I just would like to check whether is there any bug in this.\nKeep “my exact words”, தமிழ் 😀, and literal {brief}.'
  sent=[]
  for recipe in recipes:
   text=recipe['prompt'].replace('{brief}',brief)
   assert text.startswith('Your request:\n'+brief+'\n\n'+recipe['label']+' task:\n')
   target=ax('append-target',*scoped);assert target.get('ok'),target
   draft_args=['append','--text',text,'--expect-target',target['target'],'--expect-draft',target['fingerprint'],*scoped]
   result=ax(*draft_args);assert result.get('ok'),result
   # A placeholder paragraph can retain its final br. The production readback ignores
   # only surrounding whitespace; verify every internal newline and spoken character.
   actual=state()['text'];assert actual.rstrip('\n')==text and state()['sent']==sent,state()
   assert ax(*draft_args,'--accept-existing').get('method')=='existing'
   assert state()=={'text':actual,'sent':sent},state()
   assert ax('send','--expect-text',text,'--expect-target',target['target'],'--send-label','Send',*scoped).get('ok')
   sent.append(actual);assert state()=={'text':'','sent':sent},state()
  assert ax('context-clipboard').get('text')=='Unchanged fixture clipboard'
  report={'variant':variant,'status':'PASS','mode':surface_mode,'recipes':[r['id'] for r in recipes],
   'verbatimRequestFirst':True,'waitsForExplicitSend':True,'duplicateRetryPrevented':True,
   'limitation':'Synthetic transcripts and an isolated composer; physical keypad and microphone acceptance remain separate.'}
  (work/'fixture-result.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2));sys.exit(0)
 if kind in ['image','files','image-placeholder','image-placeholder-literal']:
  import base64
  scoped=['--mode-prefix','Mode: ','--expect-mode',surface_mode,'--composer-send-label','Send',*placeholder_args]
  def state(): return json.loads((work/'composer-state.json').read_text())
  target=ax('append-target',*scoped);assert target.get('ok'),target
  text='Explain this error.\nPreserve my notes: தமிழ் 😀'
  if kind=='image-placeholder-literal': text=hint
  if kind!='image-placeholder':
   result=ax('append','--text',text,'--expect-target',target['target'],'--expect-draft',target['fingerprint'],*scoped);assert result.get('ok'),result
  before=state()['text']; target=ax('append-target',*scoped);assert target.get('ok'),target
  path=work/'screenshot-fixture.png'
  path.write_bytes(base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a9WQAAAAASUVORK5CYII='))
  paths=[path]
  if kind=='files':
   paths=[work/'comparison-a.txt',work/'comparison-b.csv']
   for file in paths:file.write_text('Disposable fixture document: '+file.name)
   descriptors=[dict(Path=str(file),Size=file.stat().st_size,Modified=file.stat().st_mtime_ns//1_000_000) for file in paths]
   arguments=['attach-files','--files',json.dumps(descriptors),'--expect-target',target['target'],*scoped]
  else:arguments=['attach-image','--image',str(path),'--expect-target',target['target'],*scoped]
  result=ax(*arguments);assert result.get('ok'),result
  names=[file.name for file in paths]
  assert state()=={'text':before,'sent':[],'images':names},state()
  assert ax('context-clipboard').get('text')=='Unchanged fixture clipboard'
  duplicate=ax(*arguments)
  assert duplicate.get('error')=='attachment-name-exists' if kind=='files' else duplicate.get('ok'),duplicate
  assert state()['images']==names,state()
  target=ax('append-target',*scoped);assert target.get('ok'),target
  result=ax('append','--text','Suggest a fix.','--expect-target',target['target'],'--expect-draft',target['fingerprint'],*scoped);assert result.get('ok'),result
  expected=state()['text']
  if before.strip(): assert re.fullmatch(re.escape(before)+r'\n{2,}'+re.escape('Suggest a fix.'),expected),state()
  else: assert expected.strip()=='Suggest a fix.',state()
  retry=ax('append','--text','Suggest a fix.','--expect-target',target['target'],'--expect-draft',target['fingerprint'],'--accept-existing',*scoped)
  assert retry.get('method')=='existing' and state()['text']==expected,(retry,state())
  assert ax('context-clipboard').get('text')=='Unchanged fixture clipboard'
  assert state()['images']==names and state()['sent']==[],state()
  report={'variant':variant,'status':'PASS','versions':json.loads((work/'ready').read_text()),'state':state(),'limitation':'Controlled Chromium fixture and named pasteboard; live ChatGPT and physical keypad acceptance are separate.'}
  (work/'fixture-result.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2));sys.exit(0)
 if kind=='write':
  scoped=['--mode-prefix','Mode: ','--expect-mode',surface_mode,'--composer-send-label','Send',*placeholder_args]
  def state(): return json.loads((work/'composer-state.json').read_text())
  def write(text,retry=False,send=False,target=None):
   return ax('write','--text',text,*scoped,*(['--accept-existing'] if retry else []),*(['--send-label','Send'] if send else []),*(['--expect-target',target] if target else []))
  prompt=review_changes_prompt() + ' ' + ('Preserve every word: café தமிழ் 😀. ' * 100).strip()
  result=write(prompt);assert result.get('ok'),result
  assert state()=={'text':prompt,'sent':[]},state()
  assert write(prompt,retry=True).get('method')=='existing'
  assert write(prompt,retry=True,send=True).get('error')=='draft-exists'
  assert ax('send','--send-label','Send',*scoped).get('ok')
  assert state()=={'text':'','sent':[prompt]},state()
  target=ax('append-target',*scoped);assert target.get('ok'),target
  result=write(prompt,send=True,target=target['target']);assert result.get('ok'),result
  assert state()=={'text':'','sent':[prompt,prompt]},state()
  assert write('My existing draft.').get('ok')
  assert write(prompt,retry=True).get('error')=='draft-exists'
  assert state()=={'text':'My existing draft.','sent':[prompt,prompt]},state()
  assert ax('context-clipboard').get('text')=='Unchanged fixture clipboard'
  report={'variant':variant,'status':'PASS','versions':json.loads((work/'ready').read_text()),'characters':len(prompt),'submissions':len(state()['sent']),'existingDraftPreserved':True,'limitation':'Controlled Chromium fixture only; physical keypad acceptance remains separate.'}
  (work/'fixture-result.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2));sys.exit(0)
 if kind=='preset':
  scoped=['--mode-prefix','Mode: ','--expect-mode',surface_mode,'--composer-send-label','Send',*placeholder_args]
  def state(): return json.loads((work/'composer-state.json').read_text())
  def append(text,target,retry=False):
   return ax('append','--text',text,'--expect-target',target['target'],'--expect-draft',target['fingerprint'],*scoped,*(['--accept-existing'] if retry else []))
  def send(text,target): return ax('send','--send-label','Send','--expect-target',target['target'],'--expect-text',text,*scoped)
  prompt=review_changes_prompt()
  target=ax('append-target',*scoped);assert target.get('ok'),target
  result=append(prompt,target);assert result.get('ok'),result
  assert state()=={'text':prompt,'sent':[]},state()
  assert append(prompt,target,True).get('method')=='existing'
  assert send(prompt[:120],target).get('error')=='draft-changed'
  assert state()=={'text':prompt,'sent':[]},state()
  assert send(prompt,target).get('ok')
  assert send(prompt,target).get('error')=='no-sendable-draft'
  assert state()=={'text':'','sent':[prompt]},state()
  target=ax('append-target',*scoped);assert target.get('ok'),target
  assert append(prompt,target).get('ok')
  edited=ax('append-target',*scoped);assert edited.get('ok'),edited
  assert append('My additional instruction.',edited).get('ok')
  assert send(prompt,target).get('error')=='draft-changed'
  assert state()['sent']==[prompt] and state()['text'].endswith('My additional instruction.'),state()
  assert ax('context-clipboard').get('text')=='Unchanged fixture clipboard'
  report={'variant':variant,'status':'PASS','versions':json.loads((work/'ready').read_text()),'state':state(),'limitation':'Controlled Chromium editor and named clipboard; physical keypad acceptance remains separate.'}
  (work/'fixture-result.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2));sys.exit(0)
 if kind=='append':
  scoped=['--mode-prefix','Mode: ','--expect-mode',surface_mode,'--composer-send-label','Send',*placeholder_args]
  def append(text,target,retry=False):
   return ax('append','--text',text,'--expect-target',target['target'],'--expect-draft',target['fingerprint'],*scoped,*(['--accept-existing'] if retry else []))
  def state(): return json.loads((work/'composer-state.json').read_text())
  email='Explain the current diff. தமிழ் 😀' if surface_mode=='Codex' else 'Customer email:\nCan you deliver Friday? தமிழ் 😀'
  target=ax('append-target',*scoped);assert target.get('ok'),target
  result=append(email,target);assert result.get('ok'),result
  assert state()=={'text':email,'sent':[]},state()
  assert append(email,target,True).get('method')=='existing'
  instruction='Focus on correctness and edge cases.' if surface_mode=='Codex' else 'Reply politely and confirm Friday.'
  target=ax('append-target',*scoped);assert target.get('ok'),target
  result=append(instruction,target);assert result.get('ok'),result
  expected=state()['text']
  assert re.fullmatch(re.escape(email)+r'\n{2,}'+re.escape(instruction),expected),state()
  assert state()['sent']==[],state()
  assert append(instruction,target,True).get('method')=='existing'
  assert ax('context-clipboard').get('text')=='Unchanged fixture clipboard'
  result=ax('send','--send-label','Send',*scoped);assert result.get('ok'),result
  assert state()=={'text':'','sent':[expected]},state()
  report={'variant':variant,'status':'PASS','versions':json.loads((work/'ready').read_text()),'state':state(),'limitation':'Controlled Chromium editor with a named fixture pasteboard; real ChatGPT acceptance remains separate.'}
  (work/'fixture-result.json').write_text(json.dumps(report,indent=2)+'\n')
  print(json.dumps(report,indent=2));sys.exit(0)
 result=ax('copy-reply')
 chosen=(work/'copied').read_text() if (work/'copied').exists() else None
 expected_error={'wrapped-code-only':'reply-action-row-unrecognized','wrapped-split-row':'reply-action-row-unrecognized','wrapped-duplicate':'reply-copy-multiple','no-ack':'copy-unconfirmed','user-last':'no-answer','running':'answer-not-ready','codex-copy-two-chats':'reply-web-area-multiple','codex-copy-pending-panel':'reply-web-area-multiple','codex-copy-panel-code-only':'reply-action-not-found','codex-copy-panel-running':'answer-not-ready'}.get(variant)
 if expected_error:
  passed=result.get('error')==expected_error and ax('context-clipboard').get('text')=='Unchanged fixture clipboard' and (chosen=='reply' if variant=='no-ack' else chosen is None)
 else:
  passed=status.get('canCopyAnswer') is True and result.get('ok') is True and result.get('text')=='Full fixture response' and chosen=='reply'
 report={'variant':variant,'status':'PASS' if passed else 'FAIL','versions':json.loads((work/'ready').read_text()),'snapshot':status,'copy':result,'chosen':chosen,'limitation':'Controlled Chromium fixture; real ChatGPT acceptance remains separate.'}
 (work/'fixture-result.json').write_text(json.dumps(report,indent=2)+'\n')
 print(json.dumps(report,indent=2))
 assert passed, 'Chromium native response copy failed'
finally:
 for line in subprocess.check_output(['ps','-axo','pid=,command='],text=True).splitlines():
  pair=line.strip().split(None,1)
  if len(pair)==2 and (pair[1]==str(executable) or pair[1].startswith(str(executable)+' ')):
   os.kill(int(pair[0]),signal.SIGTERM)
 process.wait(timeout=8)
 log.close()
 if os.environ.get('VIZHI_KEEP_CHROMIUM_FIXTURE')!='1':
  # Preserve the exact generated fixture while avoiding a full Electron copy per test.
  # These paths were created in this fresh artifact directory; the supplied runtime stays.
  assert plistlib.loads(plist_path.read_bytes())['CFBundleIdentifier']==fixture_id
  shutil.copytree(source,work/'fixture-source',dirs_exist_ok=True)
  shutil.copy2(plist_path,work/'fixture-info.plist')
  shutil.rmtree(app)
  if (work/'user-data').exists(): shutil.rmtree(work/'user-data')
