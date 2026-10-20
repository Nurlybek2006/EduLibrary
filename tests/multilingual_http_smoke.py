"""Real three-culture MVC pages, validation and cookie/security checks on disposable port5399."""
from pathlib import Path
import json, html, sys
from urllib.parse import urlparse,parse_qs
import http_smoke as baseline
from phase2_http_smoke import Client,Page

ROOT=Path(__file__).resolve().parents[1]
BASE=baseline.BASE
assert urlparse(BASE).hostname in ('localhost','127.0.0.1') and urlparse(BASE).port==5399,'Use the disposable test instance on5399.'
checks=[]
baseline.RESULTS=checks
record=baseline.record
credentials=json.loads((ROOT/'appsettings.Local.json').read_text(encoding='utf-8-sig'))['SeedUsers']
locales=[('kk-KZ','kk'),('ru-RU','ru'),('en-US','en')]

def switch(client,culture,return_url='/Books?Search=SQL&Sort=year'):
    status,_,source=client.request('/Books')
    token=Page(source).token
    status,url,source=client.request('/Culture/Set',{'culture':culture,'returnUrl':return_url,'__RequestVerificationToken':token})
    record('Culture '+culture+' preserves local URL/query',status==200 and urlparse(url).path==urlparse(return_url).path and parse_qs(urlparse(url).query)==parse_qs(urlparse(return_url).query),status)
    return source

def page(client,route,language,label):
    status,url,source=client.request(route)
    record(label+' '+language+' '+route,status==200 and Page(source).lang==language and '/Account/Login' not in url and '/Account/AccessDenied' not in url,status)
    return html.unescape(source)

public=Client()
for culture,language in locales:
    source=switch(public,culture)
    for route in ['/Books','/Home/Privacy','/Account/Login','/Account/Register','/Account/ForgotPassword']:
        status,_,source=public.request(route)
        record('Anonymous '+language+' '+route,status==200 and Page(source).lang==language,status)
    source=page(public,'/Books?Search=Clean',language,'Book metadata')
    record('Commercial book title retained '+language,'Clean Code' in source)
    status,_,source=public.request('/Account/Register')
    status,_,source=public.request('/Account/Register',{'Email':'invalid-address','Password':'x','ConfirmPassword':'different','__RequestVerificationToken':Page(source).token})
    expected={'kk':'Электрондық пошта дұрыс емес.','ru':'Некорректный email.','en':'Invalid email address.'}[language]
    record('Translated registration validation '+language,status==200 and expected in html.unescape(source),status)
    status,_,source=public.request('/Account/Login')
    status,_,source=public.request('/Account/Login',{'Email':'missing-locale-user@example.invalid','Password':'WrongDemo!234','__RequestVerificationToken':Page(source).token})
    expected={'kk':'Пошта немесе құпиясөз дұрыс емес','ru':'Неверная почта или пароль','en':'The email or password is incorrect'}[language]
    record('Translated authentication failure '+language,status==200 and expected in html.unescape(source),status)
    status,_,source=public.request('/not-a-real-library-route')
    record('Translated404 '+language,status==404 and Page(source).lang==language,status)

routes={
 'Student':['/Books','/Loans','/Fines','/Students/MyHistory','/ReadingSpace','/Reservations','/Notifications','/Recommendations','/Recommendations/Preferences','/Account/Profile','/DigitalLibrary','/DigitalLibrary/My'],
 'Librarian':['/Dashboard','/Authors','/Authors/Create','/Publishers','/Publishers/Create','/Categories','/Categories/Create','/Copies','/Copies/Create','/Copies/Lookup','/Students','/Students/Create','/Loans','/Loans/Create','/Returns','/Fines','/Reservations','/Reservations/Lookup','/Recommendations','/Analytics','/Reports','/DigitalLibrary','/DigitalLibrary/Manage','/DigitalLibrary/Upload','/Account/Profile'],
 'Admin':['/Dashboard','/Books/Create','/Staff','/Staff/Create','/Reviews','/Home/Settings','/Account/Profile','/DigitalLibrary/Manage','/DigitalLibrary/Upload']
}
for role,paths in routes.items():
    client=Client()
    assert client.login(role,credentials[role])
    for culture,language in locales:
        switch(client,culture)
        for route in paths:page(client,route,language,role)
        if role in ['Admin','Librarian']:
            status,_,source=client.request('/Authors/Create')
            status,_,source=client.request('/Authors/Create',{'FirstName':'','LastName':'','__RequestVerificationToken':Page(source).token})
            expected={'kk':'Автордың атын енгізіңіз.','ru':'Введите имя автора.','en':"Enter the author's first name."}[language]
            record(role+' localized CRUD validation '+language,status==200 and expected in html.unescape(source),status)
            status,_,source=client.request('/DigitalLibrary/Upload')
            status,_,source=client.request('/DigitalLibrary/Upload',{'BookId':'0','Language':'','__RequestVerificationToken':Page(source).token})
            expected={'kk':'Бұл өрісті толтырыңыз.','ru':'Заполните это поле.','en':'Fill in this field.'}[language]
            record(role+' localized digital validation '+language,status==200 and expected in html.unescape(source),status)
    switch(client,'kk-KZ','/Books')

for return_url in ['https://example.invalid/escape','//example.invalid/escape','/\\example.invalid/escape']:
    source=switch(public,'en-US','/Books')
    status,url,_=public.request('/Culture/Set',{'culture':'en-US','returnUrl':return_url,'__RequestVerificationToken':Page(source).token})
    record('External return URL stays on library origin',status==200 and urlparse(url).netloc==urlparse(BASE).netloc and urlparse(url).path=='/Books',status)
status,_,source=public.request('/Books')
status,_,_=public.request('/Culture/Set',{'culture':'unsupported','returnUrl':'/Books','__RequestVerificationToken':Page(source).token})
record('Unsupported culture rejected',status==400,status)
status,_,_=public.request('/Culture/Set',{'culture':'ru-RU','returnUrl':'/Books'})
record('Culture switch requires CSRF',status==400,status)
report={'passed':sum(c['passed'] for c in checks),'failed':sum(not c['passed'] for c in checks),'checks':checks}
(ROOT/'artifacts/multilingual-http-results.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('MULTILINGUAL HTTP:',report['passed'],'passed,',report['failed'],'failed')
raise SystemExit(bool(report['failed']))
