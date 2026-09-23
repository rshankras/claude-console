#!/usr/bin/env python3
"""Add Ask ChatGPT to the System profile without replacing any existing page or binding.

Call with the explicit selected System ProfileInfo.json path. The plugin never runs this on
startup. This is an additive installation utility for a user-authorized profile change.
"""
import copy,json,os,pathlib,sys,zipfile,importlib.util
PAGE_ID='97DDD52079184C57BFEF7107102B5E6A'
ROOT=pathlib.Path(__file__).resolve().parent.parent

def updated_profile(current, context_page):
    if current.get('applicationName')!='@_defaultmac' or current.get('nativePluginName')!='DefaultMac':
        raise ValueError('Expected the selected System profile; no other profile may be changed')
    result=copy.deepcopy(current)
    plugins=result.setdefault('additionalNativePluginNames',[])
    if 'VizhiDesktop' not in plugins: plugins.append('VizhiDesktop')
    for mode in result['layout']['layoutModes']:
        for workspace in mode['workspaces']:
            pages=workspace['pressPages']
            if any(page.get('name')==PAGE_ID for page in pages):continue
            page=copy.deepcopy(context_page);page['name']=PAGE_ID;page['displayName']='Ask ChatGPT'
            pages.append(page)
    return result

def install(path):
    with zipfile.ZipFile(ROOT/'src/Products/VizhiDesktop/package/profiles/DefaultProfile70.lp5') as z:
        packaged=json.loads(z.read('ProfileInfo.json'))
    page=copy.deepcopy(packaged['layout']['layoutModes'][0]['workspaces'][0]['pressPages'][0])
    spec=importlib.util.spec_from_file_location('desktop_profile', ROOT/'tools/make-desktop-profile.py')
    generator=importlib.util.module_from_spec(spec);spec.loader.exec_module(generator)
    for control,binding in zip(page['controls'],generator.CONTEXT_PAGE): control['pressAction']=binding
    before=path.read_text();current=json.loads(before);result=updated_profile(current,page)
    if result==current:return False
    backup=path.with_name(path.name+'.before-context')
    if not backup.exists():backup.write_text(before)
    temporary=path.with_name(path.name+'.context-tmp');temporary.write_text(json.dumps(result,indent=2)+'\n')
    os.replace(temporary,path);return True

if __name__=='__main__':
    if len(sys.argv)!=2:raise SystemExit('Usage: install-desktop-context-page.py /explicit/System/ProfileInfo.json')
    print('Context page added' if install(pathlib.Path(sys.argv[1])) else 'Existing Context page preserved')
