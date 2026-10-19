(() => {
    const form=document.querySelector('#catalog-filter'),results=document.querySelector('#catalog-results');
    if(!form||!results)return;
    let pending,timer,sequence=0;
    const status=document.querySelector('#catalog-status');
    let view='grid';try{view=localStorage.getItem('edulibrary-catalog-view')||view;}catch{}
    const updateView=()=>{results.classList.toggle('list-view',view==='list');document.querySelectorAll('[data-catalog-view]').forEach(b=>{b.classList.toggle('active',b.dataset.catalogView===view);b.setAttribute('aria-pressed',String(b.dataset.catalogView===view));});};
    async function load(url,push=true){
        pending?.abort();pending=new AbortController();const own=++sequence;
        results.setAttribute('aria-busy','true');results.classList.add('is-fetching');status.textContent='Кітаптар ізделуде…';
        try{const endpoint=new URL('/Books/Results',location.origin);endpoint.search=new URL(url,location.origin).search;const response=await fetch(endpoint,{signal:pending.signal,headers:{'X-Requested-With':'XMLHttpRequest'}});if(!response.ok)throw new Error();const html=await response.text();if(own!==sequence)return;results.innerHTML=html;window.lucide?.createIcons();updateView();status.textContent=results.querySelector('.catalog-results-summary')?.textContent.trim()||'Іздеу аяқталды.';if(push)history.pushState(null,'',url);results.querySelectorAll('img').forEach(img=>img.addEventListener('error',()=>img.src='/images/book-placeholder.svg',{once:true}));}
        catch(error){if(error.name!=='AbortError')status.textContent='Іздеу орындалмады. Іздеу батырмасымен қайталап көріңіз.';}
        finally{if(own===sequence){results.removeAttribute('aria-busy');results.classList.remove('is-fetching');}}
    }
    function search(){const query=new URLSearchParams(new FormData(form));load('/Books?'+query);}
    form.addEventListener('submit',e=>{e.preventDefault();clearTimeout(timer);search();});
    form.querySelectorAll('select,input[type=checkbox]').forEach(input=>input.addEventListener('change',search));
    form.querySelector('[name=Search]')?.addEventListener('input',()=>{clearTimeout(timer);timer=setTimeout(search,320);});
    results.addEventListener('click',e=>{const link=e.target.closest('.pagination a');if(link){e.preventDefault();load(link.href);}});
    document.querySelectorAll('[data-catalog-view]').forEach(button=>button.addEventListener('click',()=>{view=button.dataset.catalogView;try{localStorage.setItem('edulibrary-catalog-view',view);}catch{} updateView();}));
    window.addEventListener('popstate',()=>{const query=new Map([...new URLSearchParams(location.search)].map(([key,value])=>[key.toLowerCase(),value]));for(const input of form.elements){if(!input.name)continue;const value=query.get(input.name.toLowerCase());if(input.type==='checkbox')input.checked=value?.toLowerCase()==='true';else if(input.tagName==='INPUT'||input.tagName==='SELECT')input.value=value||(input.name==='Sort'?'title':'');}load(location.href,false);});
    updateView();
})();
