"""Build standard RESX files from the reviewed translation catalogs (no network calls)."""
from pathlib import Path
import json,xml.etree.ElementTree as ET,re
root=Path(__file__).resolve().parents[1]
folder=root/'Resources'
catalog={}
for file in sorted(folder.glob('*-translations.json'),key=lambda p:(p.name!='legacy-translations.json',p.name)):
    items=json.loads(file.read_text(encoding='utf-8-sig'))
    if isinstance(items,dict):
        items=[{'key':key,**(value if isinstance(value,dict) else dict(zip(['kk','ru','en'],value)))} for key,value in items.items()]
    for item in items:
        key=item['key']
        catalog[key]={lang:item[lang] for lang in ['kk','ru','en']}
for key,value in list(catalog.items()):
    # Preserve intentional spacing in concatenated legacy labels.
    trimmed=key.strip()
    if trimmed and trimmed not in catalog:catalog[trimmed]={lang:text.strip() for lang,text in value.items()}
for key,values in catalog.items():
    if not all(values.values()):raise ValueError('Empty translation: '+key)
    expected=set(re.findall(r'\{(\d+)(?:[:,][^}]*)?\}',values['kk']))
    for lang,value in values.items():
        if expected!=set(re.findall(r'\{(\d+)(?:[:,][^}]*)?\}',value)):raise ValueError('Format placeholders differ: '+key+' '+lang)
# RESX binary writers reject case-only duplicates. Use one canonical source spelling.
aliases={};canonical={}
for key in sorted(catalog,key=lambda k:(k.isupper(),k.islower(),k)):
    norm=key.casefold()
    if norm in canonical:aliases[key]=canonical[norm]
    else:canonical[norm]=key
for key in aliases:del catalog[key]
for path in (root/'Views').rglob('*.cshtml'):
    text=path.read_text(encoding='utf-8-sig')
    updated=re.sub(r'(T\[)"([^"\r\n]+)"',lambda m:m[1]+json.dumps(aliases.get(m[2],m[2]),ensure_ascii=False),text)
    if updated!=text:path.write_text(updated,encoding='utf-8')
def write(path,entries):
    tree=ET.Element('root')
    for name,value in [('resmimetype','text/microsoft-resx'),('version','2.0'),('reader','System.Resources.ResXResourceReader, System.Windows.Forms'),('writer','System.Resources.ResXResourceWriter, System.Windows.Forms')]:
        ET.SubElement(ET.SubElement(tree,'resheader',name=name),'value').text=value
    for key,value in sorted(entries.items()):
        data=ET.SubElement(tree,'data',{'name':key,'{http://www.w3.org/XML/1998/namespace}space':'preserve'})
        ET.SubElement(data,'value').text=value
    ET.indent(tree,space='  ')
    ET.ElementTree(tree).write(path,encoding='utf-8',xml_declaration=True)
for suffix,lang in [('', 'kk'),('.kk-KZ','kk'),('.ru-RU','ru'),('.en-US','en')]:
    write(folder/('SharedResource'+suffix+'.resx'),{key:values[lang] for key,values in catalog.items()})
views=folder/'Views'/'Shared';views.mkdir(parents=True,exist_ok=True)
for suffix,value in [('', 'Тілді таңдаңыз'),('.kk-KZ','Тілді таңдаңыз'),('.ru-RU','Выберите язык'),('.en-US','Choose language')]:
    write(views/('_LanguageSwitcher'+suffix+'.resx'),{'Choose language':value})
print('Built',len(catalog),'complete trilingual resource entries')
