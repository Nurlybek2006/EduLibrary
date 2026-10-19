"""Read-only HTTP checks against a running, seeded EduLibrary instance.

Passwords are read from ignored appsettings.Local.json and never logged.
Only sign-in/sign-out requests and deliberately rejected POSTs are performed.
Run: python tests/http_smoke.py [http://localhost:5298]
"""

from __future__ import annotations

import html
import http.cookiejar
import json
from pathlib import Path
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from decimal import Decimal
from html.parser import HTMLParser


ROOT = Path(__file__).resolve().parents[1]
BASE = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5298").rstrip("/")
RESULTS: list[dict] = []


class Page(HTMLParser):
    def __init__(self, source: str):
        super().__init__(convert_charrefs=True)
        self.links: list[str] = []
        self.assets: list[str] = []
        self.token: str | None = None
        self.lang: str | None = None
        self.hidden: dict[str, str] = {}
        self.payment_max: str | None = None
        self.feed(source)

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "html":
            self.lang = attrs.get("lang")
        if tag == "a" and attrs.get("href"):
            self.links.append(attrs["href"])
        if tag == "input" and attrs.get("name") == "__RequestVerificationToken":
            self.token = attrs.get("value")
        if tag == "input" and attrs.get("type", "").lower() == "hidden" and attrs.get("name"):
            self.hidden[attrs["name"]] = attrs.get("value", "")
        if tag == "input" and attrs.get("name") == "Amount":
            self.payment_max = attrs.get("max")
        if tag == "script" and attrs.get("src", "").startswith("/"):
            self.assets.append(attrs["src"])
        if tag == "link" and attrs.get("href", "").startswith("/"):
            self.assets.append(attrs["href"])


class Client:
    def __init__(self):
        self.cookies = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.cookies))

    def request(self, path: str, data: dict | None = None):
        encoded = None if data is None else urllib.parse.urlencode(data, doseq=True).encode("utf-8")
        req = urllib.request.Request(BASE + path, data=encoded, headers={"User-Agent": "EduLibrary-ReadOnly-Smoke/1.0"})
        try:
            response = self.opener.open(req, timeout=30)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            raw = response.read()
            return response.status, response.url, raw.decode("utf-8", errors="replace")

    def login(self, role: str, credentials: dict):
        status, _, source = self.request("/Account/Login")
        token = Page(source).token
        if status != 200 or not token:
            record(f"{role}: login form and antiforgery", False, status)
            return False
        status, url, source = self.request("/Account/Login", {
            "Email": credentials["Email"], "Password": credentials["Password"],
            "RememberMe": "false", "__RequestVerificationToken": token,
        })
        okay = status == 200 and "/Account/Login" not in url and any(c.name == "EduLibrary.Identity" for c in self.cookies)
        record(f"{role}: sign in", okay, status)
        return okay


def record(label: str, okay: bool, status: int | None = None, detail: str | None = None):
    result = {"check": label, "passed": bool(okay)}
    if status is not None:
        result["status"] = status
    if detail:
        result["detail"] = detail
    RESULTS.append(result)
    print(f"{'PASS' if okay else 'FAIL'} {label}" + (f" [{status}]" if status is not None else ""), flush=True)


def check(client: Client, path: str, role: str = "Admin", status: int = 200, contains: str | None = None):
    actual, url, source = client.request(path)
    okay = actual == status
    if status == 200:
        okay = okay and "/Account/Login" not in url and "/Account/AccessDenied" not in url and Page(source).lang == "kk"
    if contains:
        okay = okay and contains in html.unescape(source)
    record(f"{role}: GET {path}", okay, actual)
    return source, Page(source), url


def ids(page: Page, prefix: str) -> list[int]:
    result = []
    for link in page.links:
        match = re.match(re.escape(prefix) + r"/(\d+)(?:\?|$)", link, re.IGNORECASE)
        if match and int(match.group(1)) not in result:
            result.append(int(match.group(1)))
    return result


def run():
    config = json.loads((ROOT / "appsettings.Local.json").read_text(encoding="utf-8-sig"))
    credentials = config["SeedUsers"]
    anonymous = Client()
    catalog_source, catalog, _ = check(anonymous, "/Books", "Anonymous", contains="Кітаптар каталогы")
    book_ids = ids(catalog, "/Books/Details")
    if not book_ids:
        record("Seeded catalog has book links", False)
        return
    book_id = book_ids[0]
    check(anonymous, f"/Books/Details/{book_id}", "Anonymous")
    check(anonymous, "/Home/Privacy", "Anonymous")
    for path in ["/Account/Login", "/Account/Register"]:
        status, _, source = anonymous.request(path)
        record(f"Anonymous: GET {path}", status == 200 and bool(Page(source).token), status)
    for path in ["/Dashboard", "/Authors", "/Account/Profile"]:
        status, url, _ = anonymous.request(path)
        record(f"Anonymous: challenge {path}", status == 200 and "/Account/Login" in url, status)
    check(anonymous, "/Books/Details/2147483647", "Anonymous", status=404)

    admin = Client()
    if not admin.login("Admin", credentials["Admin"]):
        return
    check(admin, "/")
    check(admin, "/Dashboard", contains="Басқару тақтасы")
    _, full_catalog, _ = check(admin, "/Books")
    book_id = ids(full_catalog, "/Books/Details")[0]
    check(admin, f"/Books/Details/{book_id}")
    for query in ["Search=Clean", "Search=9780001000000", "AuthorId=1", "PublisherId=1", "CategoryId=1", "Language=English", "Sort=year", "Sort=popularity", "Page=2", "Page=-5", "Page=999999", "IncludeArchived=true", "Search=not-a-real-book-67293", "Search=%27%20OR%201%3D1--"]:
        check(admin, "/Books?" + query)
    _, _, isbn_source = admin.request("/Books?Search=9780001000000")
    isbn_ids = ids(Page(isbn_source), "/Books/Details")
    record("Catalog: normalized ISBN finds exactly Clean Code", len(isbn_ids) == 1 and "Clean Code" in html.unescape(isbn_source))
    _, _, empty_source = admin.request("/Books?Search=not-a-real-book-67293")
    record("Catalog: unmatched search returns empty state", not ids(Page(empty_source), "/Books/Details") and "Кітап табылмады" in html.unescape(empty_source))
    _, _, year_source = admin.request("/Books?Sort=year")
    # The editorial cover keeps publication year visible in its edition line.
    # Verify actual rendered metadata order, independently of the former card CSS.
    cards = re.findall(r'<article class="editorial-book-card"[^>]*>(.*?)</article>', year_source, re.DOTALL)
    years = [int(match.group(1)) for card in cards if (match := re.search(r'class="book-jacket-edition">(\d{4})', card))]
    record("Catalog: publication years descend", len(years) > 1 and years == sorted(years, reverse=True))
    _, _, page_two_source = admin.request("/Books?Page=2")
    record("Catalog: second page contains distinct books", bool(ids(Page(page_two_source), "/Books/Details")) and not set(ids(full_catalog, "/Books/Details")).intersection(ids(Page(page_two_source), "/Books/Details")))
    _, _, negative_page_source = admin.request("/Books?Page=-5")
    record("Catalog: negative page clamps to first page", ids(Page(negative_page_source), "/Books/Details") == ids(full_catalog, "/Books/Details"))
    for path in ["/Books/Create", f"/Books/Edit/{book_id}"]:
        check(admin, path)
    for controller in ["Authors", "Publishers", "Categories"]:
        _, page, _ = check(admin, "/" + controller)
        check(admin, "/" + controller + "?search=not-found-67293")
        check(admin, "/" + controller + "/Create")
        edit_ids = ids(page, f"/{controller}/Edit")
        record(f"{controller}: seeded editable reference", bool(edit_ids))
        if edit_ids:
            check(admin, f"/{controller}/Edit/{edit_ids[0]}")
    _, copies, _ = check(admin, "/Copies")
    editable_copies = ids(copies, "/Copies/Edit")
    for path in ["/Copies?status=0", "/Copies?status=1", f"/Copies?bookId={book_id}", "/Copies?search=EL-", f"/Copies/Create?bookId={book_id}"]:
        check(admin, path)
    if editable_copies:
        check(admin, f"/Copies/Edit/{editable_copies[0]}")

    _, students, _ = check(admin, "/Students")
    student_ids = ids(students, "/Students/Details")
    for path in ["/Students?Search=ST2026", "/Students?Course=1", "/Students?GroupName=IT", "/Students/Create"]:
        check(admin, path)
    if student_ids:
        check(admin, f"/Students/Details/{student_ids[0]}")
        check(admin, f"/Students/Edit/{student_ids[0]}")

    _, active_loans, _ = check(admin, "/Loans?status=active")
    active_ids = ids(active_loans, "/Loans/Details")
    all_loan_ids = set(active_ids)
    for query in ["", "?status=overdue", "?status=returned", "?status=all", "?search=Clean", "?page=2"]:
        _, loans, _ = check(admin, "/Loans" + query)
        all_loan_ids.update(ids(loans, "/Loans/Details"))
    check(admin, "/Loans/Create")
    if editable_copies:
        check(admin, f"/Loans/Create?copyId={editable_copies[0]}")
    if active_ids:
        check(admin, f"/Loans/Details/{active_ids[0]}")
        check(admin, f"/Returns/Create/{active_ids[0]}")
    check(admin, "/Returns")
    check(admin, "/Returns?search=Clean")
    _, fines, _ = check(admin, "/Fines?status=all")
    fine_ids = ids(fines, "/Fines/Details")
    for status in ["outstanding", "partial", "paid"]:
        check(admin, "/Fines?status=" + status)
    if fine_ids:
        check(admin, f"/Fines/Details/{fine_ids[0]}")
        _, _, fine_source = admin.request(f"/Fines/Details/{fine_ids[0]}")
        fine_page = Page(fine_source)
        payment_ready = bool(fine_page.token and fine_page.hidden.get("FineId") and fine_page.hidden.get("RowVersion") and fine_page.payment_max)
        if payment_ready:
            # Guard the fixture before sending even a deliberately rejected payment.
            payment_ready = Decimal("0") < Decimal(fine_page.payment_max) < Decimal("999999.99")
        record("Payment: safe unpaid fixture with CSRF and RowVersion", payment_ready)
        if payment_ready:
            original_version = fine_page.hidden["RowVersion"]
            for amount, expected_error, label in [
                ("999999.99", "Төлем сомасы қалған берешектен аспауы тиіс.", "decimal point reaches balance validation"),
                ("999999,99", "Төлем сомасы қалған берешектен аспауы тиіс.", "decimal comma reaches balance validation"),
                ("0.015", "екі ондық таңбадан аспауы тиіс", "third decimal reaches precision validation"),
            ]:
                status, _, response_source = admin.request("/Fines/Pay", {
                    "__RequestVerificationToken": fine_page.token,
                    "FineId": fine_page.hidden["FineId"],
                    "RowVersion": fine_page.hidden["RowVersion"],
                    "Amount": amount,
                })
                record("Payment: " + label, status == 200 and expected_error in html.unescape(response_source), status)
            _, _, after_source = admin.request(f"/Fines/Details/{fine_ids[0]}")
            after_page = Page(after_source)
            record("Payment: all rejected requests preserve balance and version", after_page.hidden.get("RowVersion") == original_version and after_page.payment_max == fine_page.payment_max)
    for path in ["/Staff", "/Staff/Create", "/Account/Profile", "/Home/Settings", "/Home/Privacy", "/Home/Intelligence?section=recommendations", "/Home/Intelligence?section=analytics", "/Home/Intelligence?section=reports"]:
        check(admin, path)

    for asset in dict.fromkeys(catalog.assets):
        status, _, body = admin.request(asset)
        record("Static asset: " + asset.split("?")[0], status == 200 and bool(body), status)

    for path in [f"/Books/Archive/{book_id}", "/Account/Logout", "/Fines/Pay"]:
        status, _, _ = admin.request(path, {})
        record("Admin: rejects POST without CSRF " + path, status == 400, status)
    check(admin, "/Account/Profile")

    student = Client()
    if not student.login("Student", credentials["Student"]):
        return
    for path in ["/Books", f"/Books/Details/{book_id}", "/Account/Profile", "/Loans", "/Fines", "/Home/Intelligence?section=recommendations"]:
        check(student, path, "Student")
    _, _, history_url = check(student, "/Students/MyHistory", "Student")
    own_match = re.search(r"/Students/Details/(\d+)", history_url, re.IGNORECASE)
    own_student_id = int(own_match.group(1)) if own_match else None
    _, own_loans_page, _ = check(student, "/Loans?status=all", "Student")
    own_loan_ids = set(ids(own_loans_page, "/Loans/Details"))
    for loan_id in own_loan_ids:
        check(student, f"/Loans/Details/{loan_id}", "Student")
    _, own_fines_page, _ = check(student, "/Fines?status=all", "Student")
    own_fine_ids = set(ids(own_fines_page, "/Fines/Details"))
    foreign_students = [i for i in student_ids if i != own_student_id]
    foreign_loans = sorted(all_loan_ids - own_loan_ids)
    foreign_fines = [i for i in fine_ids if i not in own_fine_ids]
    record("Student: foreign student fixture available", bool(foreign_students))
    record("Student: foreign loan fixture available", bool(foreign_loans))
    record("Student: foreign fine fixture available", bool(foreign_fines))
    if foreign_students:
        check(student, f"/Students/Details/{foreign_students[0]}", "Student", status=404)
    if foreign_loans:
        check(student, f"/Loans/Details/{foreign_loans[0]}", "Student", status=404)
    if foreign_fines:
        check(student, f"/Fines/Details/{foreign_fines[0]}", "Student", status=404)
    for path in ["/Dashboard", "/Authors", "/Authors/Create", "/Publishers", "/Categories", "/Books/Create", f"/Books/Edit/{book_id}", "/Copies", "/Students", "/Staff", "/Staff/Create", "/Home/Settings", "/Loans/Create", "/Returns"]:
        check(student, path, "Student", status=403)
    if active_ids:
        check(student, f"/Returns/Create/{active_ids[0]}", "Student", status=403)
    _, _, profile_source = student.request("/Account/Profile")
    student_token = Page(profile_source).token
    for path in ["/Books/Archive/2147483647", "/Authors/Delete/2147483647", "/Publishers/Delete/2147483647", "/Categories/Delete/2147483647", "/Copies/Delete/2147483647", "/Students/Deactivate/2147483647", "/Fines/Pay", "/Staff/Deactivate"]:
        status, _, _ = student.request(path, {"__RequestVerificationToken": student_token, "id": "2147483647", "FineId": "2147483647"})
        record("Student: refuses staff POST " + path, status == 403, status)
    status, url, _ = student.request("/Account/Logout", {"__RequestVerificationToken": student_token})
    record("Student: sign out", status == 200 and "/Account/Login" in url, status)
    status, url, _ = student.request("/Account/Profile")
    record("Student: signed-out profile challenged", status == 200 and "/Account/Login" in url, status)


if __name__ == "__main__":
    try:
        run()
    except Exception as error:
        # Do not emit request payloads, credentials, cookies, or response bodies.
        record("HTTP smoke runner completed", False, detail=type(error).__name__)
    passed = sum(result["passed"] for result in RESULTS)
    failed = len(RESULTS) - passed
    report = {"base_url": BASE, "passed": passed, "failed": failed, "checks": RESULTS}
    (ROOT / "tests" / "http_smoke.results.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"HTTP SMOKE: {passed} passed, {failed} failed", flush=True)
    sys.exit(1 if failed else 0)
