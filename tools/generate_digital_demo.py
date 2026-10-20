"""Build the five ORIGINAL Kazakh learning booklets and their matching EPUBs.

No external textbook content is used. Run from the repository root after installing
reportlab, pypdf and PyMuPDF into .tools/pdf-runtime. Noto fonts are SIL OFL licensed.
"""
from pathlib import Path
import sys, json, html, zipfile, hashlib, uuid
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / '.tools/pdf-runtime'))
from reportlab.pdfgen import canvas
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, PageBreak, Preformatted, KeepTogether
from reportlab.lib import colors
from reportlab.lib.styles import ParagraphStyle
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from pypdf import PdfReader
import pymupdf

OUT = ROOT / 'Data/DigitalDemo'
COVERS = ROOT / 'wwwroot/images/digital-demo'
REVIEW = ROOT / 'tmp/pdfs/digital-demo'
for directory in [OUT, COVERS, REVIEW]: directory.mkdir(parents=True, exist_ok=True)
pdfmetrics.registerFont(TTFont('Demo', str(ROOT / '.tools/demo-fonts/NotoSans-Regular.ttf')))
pdfmetrics.registerFont(TTFont('DemoBold', str(ROOT / '.tools/demo-fonts/NotoSans-Bold.ttf')))
pdfmetrics.registerFontFamily('Demo', normal='Demo', bold='DemoBold')
PAPER = (595.276, 841.89)
FOREST = colors.HexColor('#18382F')
CREAM = colors.HexColor('#F7F5EF')
INK = colors.HexColor('#283A33')
STYLES = {
 'title': ParagraphStyle('title',fontName='DemoBold',fontSize=28,leading=39,textColor=colors.white,spaceAfter=23),
 'cover': ParagraphStyle('cover',fontName='Demo',fontSize=12,leading=21,textColor=colors.HexColor('#E6ECE5'),spaceAfter=17),
 'h1': ParagraphStyle('h1',fontName='DemoBold',fontSize=23,leading=32,textColor=FOREST,spaceAfter=24),
 'h2': ParagraphStyle('h2',fontName='DemoBold',fontSize=11,leading=17,textColor=FOREST,spaceBefore=18,spaceAfter=9),
 'p': ParagraphStyle('p',fontName='Demo',fontSize=10.5,leading=18,textColor=INK,spaceAfter=13),
 'small': ParagraphStyle('small',fontName='Demo',fontSize=9,leading=15,textColor=INK,spaceAfter=10),
 'code': ParagraphStyle('code',fontName='Demo',fontSize=8.5,leading=13,textColor=FOREST,backColor=CREAM,borderPadding=12,spaceBefore=10,spaceAfter=21),
 'task': ParagraphStyle('task',fontName='Demo',fontSize=10,leading=17,textColor=FOREST,backColor=CREAM,borderPadding=13,spaceBefore=9,spaceAfter=16)
}
def para(text, kind='p'): return Paragraph(html.escape(text).replace('\n','<br/>'),STYLES[kind])
def page_decor(book):
 def draw(c, doc):
  c.saveState()
  if doc.page == 1:
   c.setFillColor(FOREST); c.rect(0,0,*PAPER,fill=1,stroke=0)
   c.setFillColor(colors.HexColor(book['accent'])); c.rect(0,0,16,PAPER[1],fill=1,stroke=0)
   c.setStrokeColor(colors.HexColor('#658375')); c.setLineWidth(.7); c.line(51,735,544,735); c.line(51,119,544,119)
   c.setFillColor(colors.HexColor('#D6DFD5')); c.setFont('DemoBold',9); c.drawString(51,761,'EDULIBRARY  /  ТҮПНҰСҚА ОҚУ МАТЕРИАЛЫ')
   c.setFont('Demo',10); c.drawString(51,90,'Қысқаша сабақтар  ·  2026  ·  Қазақша')
  else:
   c.setStrokeColor(colors.HexColor('#DBE1D9')); c.line(51,786,544,786); c.line(51,44,544,44)
   c.setFillColor(colors.HexColor('#69786F')); c.setFont('Demo',8)
   c.drawString(51,799,book['title']); c.drawString(51,27,'EduLibrary Demo Content  /  CC BY 4.0')
   c.drawRightString(544,27,str(doc.page))
  c.restoreState()
 return draw

def build_pdf(book):
 path = OUT / (book['slug']+'.pdf')
 story=[Spacer(1,104),para(book['title'],'title'),para('EduLibrary Demo Content','cover'),Spacer(1,20),para(book['description'],'cover')]
 story += [PageBreak(),para('Оқу жолы','h1'),para('Бұл материалды қалай оқу керек','h2'),para('Әр сабақтағы ұғымды оқып, мысалды қолмен жүргізіңіз. Содан кейін тәжірибе тапсырмасын өзіңіз орындап, соңғы беттегі түсіндірмемен салыстырыңыз. Код үзінділері оқу үшін ықшамдалған; олар толық өндірістік жүйені алмастырмайды.')]
 for chapter in book['chapters']: story += [para(chapter['title'])]
 story += [para('Шағын жоба және жауаптарды тексеру'),para('Авторлық құқық және шығу тегі','h2'),para('Автор: EduLibrary Demo Content. 2026 жылы EduLibrary демонстрациясы үшін жаңадан жазылған түпнұсқа қазақша сабақтар. Коммерциялық кітаптан көшірілген мәтін, тарау не файл қолданылған жоқ. Бұл материал бес дербес шағын оқу құралының бірі.','small'),para('Лицензия: Creative Commons Attribution 4.0 International (CC BY 4.0). Авторды және осы лицензияны көрсетіп пайдалануға болады. Лицензия: https://creativecommons.org/licenses/by/4.0/ . Бастапқы мәтін мен генератор осы жобаның Data/DigitalDemo/content.json және tools/generate_digital_demo.py файлдарында сақталған.','small')]
 for chapter in book['chapters']:
  story += [PageBreak(),para(chapter['title'],'h1')]
  story += [para(text) for text in chapter['paragraphs']]
  story += [para('Мысалды талдау','h2'),Preformatted(chapter['code'],STYLES['code']),para('Өзіңіз орындаңыз','h2'),para(chapter['task'],'task')]
 story += [PageBreak(),para('Жоба және өзіндік тексеру','h1'),para(book['project']),para('Тапсырмаларға түсіндірме','h2')]
 for index,chapter in enumerate(book['chapters'],1): story += [para(str(index)+'. '+chapter['answer'],'small')]
 story += [para('Келесі қадам: жобаңызды басқа адамға түсіндіріп көріңіз. Қандай кіріс қабылданады, қандай ереже қорғалады және нәтижені қалай тексересіз?','small')]
 doc=SimpleDocTemplate(str(path),pagesize=PAPER,rightMargin=51,leftMargin=51,topMargin=75,bottomMargin=63,title=book['title'],author='EduLibrary Demo Content',subject=book['description'],pageCompression=1)
 doc.build(story,onFirstPage=page_decor(book),onLaterPages=page_decor(book))
 reader=PdfReader(path)
 assert len(reader.pages)>=8 and ''.join(book['title'].split()) in ''.join(reader.pages[0].extract_text().split())
 rendered=pymupdf.open(path)
 for page_no in [0,2,len(rendered)-1]:
  rendered[page_no].get_pixmap(matrix=pymupdf.Matrix(1.2,1.2)).save(str(REVIEW/f"{book['slug']}-{page_no+1}.png"))
 rendered[0].get_pixmap(matrix=pymupdf.Matrix(.6,.6)).save(str(COVERS/(book['slug']+'.png')))
 return len(reader.pages)

def xhtml(title,body):
 return '<?xml version="1.0" encoding="utf-8"?><html xmlns="http://www.w3.org/1999/xhtml" lang="kk"><head><title>'+html.escape(title)+'</title><link rel="stylesheet" href="style.css" type="text/css"/></head><body>'+body+'</body></html>'
def p(text):return '<p>'+html.escape(text)+'</p>'
def build_epub(book):
 identifier='urn:uuid:'+str(uuid.uuid5(uuid.NAMESPACE_URL,'edulibrary-original-demo/'+book['slug']))
 pages=[('cover','Мұқаба', '<h1>'+html.escape(book['title'])+'</h1>'+p('EduLibrary Demo Content')+'<img src="cover.png" alt="'+html.escape(book['title'])+'"/>'+p(book['description'])),('intro','Оқу жолы','<h1>Оқу жолы</h1>'+p('Әр тараудағы мысалды жүргізіп, тәжірибе тапсырмасын орындаңыз. Бұл - EduLibrary үшін жаңадан жазылған түпнұсқа қысқаша оқу материалы. Коммерциялық кітаптан көшірілмеген.')+p('Автор: EduLibrary Demo Content. Лицензия: Creative Commons Attribution 4.0 International. https://creativecommons.org/licenses/by/4.0/')+p('Бастапқы мәтін: Data/DigitalDemo/content.json. Генератор: tools/generate_digital_demo.py. 2026 жылғы басылым.'))]
 for index,ch in enumerate(book['chapters'],1):
  body='<h1>'+html.escape(ch['title'])+'</h1>'+''.join(p(t) for t in ch['paragraphs'])+'<h2>Мысалды талдау</h2><pre>'+html.escape(ch['code'])+'</pre><h2>Өзіңіз орындаңыз</h2>'+p(ch['task'])
  pages.append((f'chapter{index}',ch['title'],body))
 body='<h1>Жоба және өзіндік тексеру</h1>'+p(book['project'])+'<h2>Тапсырмаларға түсіндірме</h2>'
 body+=''.join(p(str(i)+'. '+ch['answer']) for i,ch in enumerate(book['chapters'],1))
 pages.append(('practice','Жоба және жауаптар',body))
 manifest='<item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/><item id="css" href="style.css" media-type="text/css"/><item id="cover-image" href="cover.png" media-type="image/png" properties="cover-image"/><item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>'
 manifest+=''.join(f'<item id="{slug}" href="{slug}.xhtml" media-type="application/xhtml+xml"/>' for slug,_,_ in pages)
 opf='<?xml version="1.0" encoding="utf-8"?><package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="uid"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="uid">'+identifier+'</dc:identifier><dc:title>'+html.escape(book['title'])+'</dc:title><dc:creator>EduLibrary Demo Content</dc:creator><dc:language>kk</dc:language><dc:description>'+html.escape(book['description'])+'</dc:description><dc:rights>CC BY 4.0 - original EduLibrary demonstration content</dc:rights><meta property="dcterms:modified">2026-10-20T00:00:00Z</meta></metadata><manifest>'+manifest+'</manifest><spine toc="ncx">'+''.join(f'<itemref idref="{slug}"/>' for slug,_,_ in pages)+'</spine></package>'
 nav='<nav xmlns:epub="http://www.idpf.org/2007/ops" epub:type="toc" id="toc"><h1>Мазмұны</h1><ol>'+''.join('<li><a href="'+slug+'.xhtml">'+html.escape(title)+'</a></li>' for slug,title,_ in pages)+'</ol></nav>'
 ncx='<?xml version="1.0" encoding="utf-8"?><ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1"><head><meta name="dtb:uid" content="'+identifier+'"/></head><docTitle><text>'+html.escape(book['title'])+'</text></docTitle><navMap>'+''.join(f'<navPoint id="n{i}" playOrder="{i}"><navLabel><text>{html.escape(title)}</text></navLabel><content src="{slug}.xhtml"/></navPoint>' for i,(slug,title,_) in enumerate(pages,1))+'</navMap></ncx>'
 with zipfile.ZipFile(OUT/(book['slug']+'.epub'),'w',zipfile.ZIP_DEFLATED) as archive:
  archive.writestr('mimetype','application/epub+zip',compress_type=zipfile.ZIP_STORED)
  archive.writestr('META-INF/container.xml','<?xml version="1.0"?><container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="OEBPS/package.opf" media-type="application/oebps-package+xml"/></rootfiles></container>')
  archive.writestr('OEBPS/package.opf',opf);archive.writestr('OEBPS/nav.xhtml',xhtml('Мазмұны',nav));archive.writestr('OEBPS/toc.ncx',ncx)
  archive.writestr('OEBPS/style.css','body{font-family:serif;line-height:1.75;margin:6%;color:#26382f}h1,h2{line-height:1.35}h1{font-size:1.7em}h2{font-size:1.1em;margin-top:1.7em}p{margin:0 0 1em}pre{white-space:pre-wrap;overflow-wrap:anywhere;font-size:.85em;padding:1em;background:#f3f2eb}img{max-width:75%;height:auto;display:block;margin:1.5em auto}a{color:#28543f}')
  archive.write(COVERS/(book['slug']+'.png'),'OEBPS/cover.png')
  for slug,title,body in pages:archive.writestr('OEBPS/'+slug+'.xhtml',xhtml(title,body))

books=json.loads((OUT/'content.json').read_text(encoding='utf-8'))
manifest=[]
for book in books:
 count=build_pdf(book);build_epub(book)
 entry={key:book[key] for key in ['slug','title','isbn','category','subject','description']}
 entry['pageCount']=count;entry['coverImageUrl']='/images/digital-demo/'+book['slug']+'.png'
 entry['files']=[{'format':fmt.upper(),'fileName':book['slug']+'.'+fmt,'sha256':hashlib.sha256((OUT/(book['slug']+'.'+fmt)).read_bytes()).hexdigest()} for fmt in ['pdf','epub']]
 manifest.append(entry)
 print(book['slug'],count,'PDF pages; matching EPUB generated and validated')
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
