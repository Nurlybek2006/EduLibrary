"""Phase 2 read-only HTTP and deliberately rejected POST checks.

Demo passwords are read from the ignored local configuration and never logged.
Run against the running app: python tests/phase2_http_smoke.py [base URL]
Successful content-changing POSTs are intentionally excluded.
"""
from __future__ import annotations

import csv
import html
import io
import json
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

import http_smoke as baseline

ROOT = Path(__file__).resolve().parents[1]
BASE = baseline.BASE
RESULTS: list[dict] = []
baseline.RESULTS = RESULTS
record = baseline.record


class Page(baseline.Page):
    def __init__(self, source):
        self.forms = []
        self.options = {}
        self.selected = None
        self.images = []
        super().__init__(source)

    def handle_starttag(self, tag, attrs):
        super().handle_starttag(tag, attrs)
        attrs = dict(attrs)
        if tag == "form":
            self.forms.append(attrs.get("action", ""))
        if tag == "select":
            self.selected = attrs.get("name")
            self.options.setdefault(self.selected, [])
        if tag == "option" and self.selected and attrs.get("value"):
            self.options[self.selected].append(attrs["value"])
        if tag == "img" and attrs.get("src", "").startswith("/"):
            self.images.append(attrs["src"])

    def handle_endtag(self, tag):
        if tag == "select":
            self.selected = None


class Client(baseline.Client):
    def raw(self, path, data=None):
        encoded = None if data is None else urllib.parse.urlencode(data, doseq=True).encode("utf-8")
        req = urllib.request.Request(BASE + path, data=encoded, headers={"User-Agent": "EduLibrary-Phase2-ReadOnly-Smoke/1.0"})
        try:
            response = self.opener.open(req, timeout=45)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            return response.status, response.url, response.read(), dict(response.headers.items())


def visit(client, path, role, status=200, contains=None):
    actual, url, source = client.request(path)
    parsed = Page(source)
    okay = actual == status
    if status == 200:
        okay = okay and parsed.lang == "kk" and "/Account/Login" not in url and "/Account/AccessDenied" not in url
    if contains:
        okay = okay and contains in html.unescape(source)
    record(f"{role}: GET {path}", okay, actual)
    return source, parsed, url


def post_rejected(client, path, data, role, expected=400, contains=None):
    status, _, source = client.request(path, data)
    record(f"{role}: rejected POST {path}", status == expected and (contains is None or contains in html.unescape(source)), status)
    return source


def query(values):
    return urllib.parse.urlencode(values)


def recommendation_context(client, path, label, expected, expected_score=None):
    # Signed relevance tokens are viewer-bound context. Never put their URL,
    # query string, payload, or response body in the console or JSON results.
    status, url, source = client.request(path)
    has_banner = 'class="recommendation-context"' in source
    okay = status == 200 and Page(source).lang == "kk" and has_banner == expected
    if expected and expected_score is not None:
        banner = re.search(r'class="recommendation-context".*?<strong>([\d.,]+)%', source, re.DOTALL)
        okay = okay and bool(banner) and float(banner.group(1).replace(",", ".")) == expected_score
    record("Signed recommendation: " + label, okay, status)


def run():
    credentials = json.loads((ROOT / "appsettings.Local.json").read_text(encoding="utf-8-sig"))["SeedUsers"]
    anonymous = Client()
    for path in ["/Recommendations", "/Recommendations/Preferences", "/Analytics", "/Analytics/Data", "/Reports", "/Reports/Export", "/Reports/Print", "/Notifications", "/Copies/Lookup", "/Copies/Details/1", "/Copies/Qr/1", "/Copies/Label/1", "/Reviews", "/Reviews/Create?bookId=1", "/Reviews/Edit/1"]:
        status, url, _ = anonymous.request(path)
        record("Anonymous: Phase 2 login challenge " + path, status == 200 and "/Account/Login" in url, status)
    source, catalog, _ = visit(anonymous, "/Books", "Anonymous")
    book_ids = baseline.ids(catalog, "/Books/Details")
    record("Phase 2 fixture: public catalog has real book IDs", bool(book_ids))
    if not book_ids:
        return
    book_id = book_ids[0]
    visit(anonymous, f"/Books/Details/{book_id}", "Anonymous", contains="Пікір")
    for search in ["Clean", "9780001000000", "Pearson", "Python", "English", "деректер", "алгоритм"]:
        status, _, body = anonymous.request("/Books/Results?" + query({"Search": search}))
        record("Catalog: AJAX results accept search " + search, status == 200 and 'data-result-count="' in body, status)
    for suffix in ["Sort=rating", "Sort=popularity&AvailableOnly=true", "SubjectId=1", "Search=%27%20OR%201%3D1--", "Page=999999"]:
        visit(anonymous, "/Books?" + suffix, "Anonymous")

    clients = {}
    staff_students = []
    all_assets = set(catalog.assets)
    copy_id = None
    copy_barcode = None
    for role in ["Admin", "Librarian"]:
        client = Client()
        if not client.login(role, credentials[role]):
            continue
        clients[role] = client
        _, staff_recs, _ = visit(client, "/Recommendations", role, contains="Студентті таңдаңыз")
        missing_reader_preferences = [link for link in staff_recs.links
            if urllib.parse.urlparse(link).path.lower() == "/recommendations/preferences"
            and not urllib.parse.parse_qs(urllib.parse.urlparse(link).query).get("studentId")]
        record(role + ": profile menu avoids preferences link without selected reader", not missing_reader_preferences)
        selected_students = [int(value) for value in staff_recs.options.get("StudentId", []) if value.isdigit()]
        staff_students = sorted(set(staff_students + selected_students))
        record(role + ": recommendations selector contains database student records", len(selected_students) >= 15)
        if selected_students:
            selected_id = selected_students[0]
            recommendations, rec_page, _ = visit(client, f"/Recommendations?studentId={selected_id}", role)
            record(role + ": recommendation explanations and factor breakdown render", "Неліктен осы кітап?" in html.unescape(recommendations) and "Сәйкестік қалай есептелді?" in html.unescape(recommendations))
            visit(client, f"/Recommendations/Preferences?studentId={selected_id}", role, contains="Оқу бағытыңыз")
            visit(client, f"/Recommendations?studentId={selected_id}&includePreviouslyRead=true", role)
            all_assets.update(rec_page.assets)
        visit(client, "/Recommendations?studentId=2147483647", role, status=404)
        visit(client, "/Recommendations/Preferences?studentId=2147483647", role, status=404)
        visit(client, "/Notifications", role)
        visit(client, "/Notifications?unreadOnly=true&page=999999", role)
        for path in ["/Copies/Lookup", "/Copies/Lookup?code=not-a-real-code-4729", "/Copies/Lookup?code=https%3A%2F%2Fevil.example%2Fcopy%2F1"]:
            visit(client, path, role)
        _, copy_list, _ = visit(client, "/Copies", role)
        copy_ids = baseline.ids(copy_list, "/Copies/Details") or baseline.ids(copy_list, "/Copies/Edit")
        record(role + ": physical-copy fixture exists", bool(copy_ids))
        if copy_ids:
            copy_id = copy_ids[0]
            details, _, _ = visit(client, f"/Copies/Details/{copy_id}", role)
            barcode = re.search(r"<code>([^<]+)</code>", details)
            copy_barcode = html.unescape(barcode.group(1)) if barcode else None
            record(role + ": copy details expose its real inventory barcode", bool(copy_barcode))
            label, label_page, _ = visit(client, f"/Copies/Label/{copy_id}", role, contains="Түгендеу жапсырмасы")
            record(role + ": inventory label references only its copy QR", f"/Copies/Qr/{copy_id}" in label and "inventory-label" in label)
            actual, _, svg, headers = client.raw(f"/Copies/Qr/{copy_id}")
            record(role + ": QR endpoint returns protected SVG", actual == 200 and b"<svg" in svg and "image/svg+xml" in headers.get("Content-Type", "") and "sandbox" in headers.get("Content-Security-Policy", ""), actual)
            if copy_barcode:
                _, _, resolved_url = visit(client, "/Copies/Lookup?" + query({"code": copy_barcode}), role)
                record(role + ": barcode lookup resolves exact physical copy", urllib.parse.urlparse(resolved_url).path.lower() == f"/copies/details/{copy_id}")
            all_assets.update(label_page.assets)
        for path in ["/Copies/Details/2147483647", "/Copies/Qr/2147483647", "/Copies/Label/2147483647"]:
            visit(client, path, role, status=404)
        analytics_html, analytics_page, _ = visit(client, "/Analytics", role, contains="Кеңейтілген талдау")
        record(role + ": analytics HTML exposes seven chart canvases", len(re.findall(r'<canvas id="chart-', analytics_html)) == 7)
        all_assets.update(analytics_page.assets)
        period_data = []
        for period in ["7", "30", "90", "year"]:
            status, _, body = client.request("/Analytics/Data?Range=" + period)
            data = json.loads(body) if status == 200 else {}
            period_data.append(data)
            record(role + ": analytics JSON period " + period, status == 200 and len(data.get("statistics", [])) == 10 and len(data.get("charts", [])) == 7, status)
            record(role + ": chart arrays align with real nonnegative counts " + period, bool(data.get("charts")) and all(len(chart["labels"]) == len(chart["values"]) and all(value >= 0 for value in chart["values"]) for chart in data["charts"]))
        if period_data and period_data[1]:
            data = period_data[1]
            start, end = data["startDate"][:10], data["endDate"][:10]
            status, _, body = client.request("/Analytics/Data?" + query({"Range": "custom", "StartDate": start, "EndDate": end}))
            custom = json.loads(body) if status == 200 else {}
            record(role + ": equivalent custom analytics range returns same series", custom.get("charts") == data["charts"], status)
            record(role + ": existing loans produce actual analytics values", sum(next(chart for chart in data["charts"] if chart["id"] == "loans")["values"]) > 0)
        for report_type in ["loans", "popular", "overdue", "readers", "ratings"]:
            report_html, report_page, _ = visit(client, "/Reports?" + query({"Type": report_type, "Range": "90"}), role)
            record(role + ": " + report_type + " report has summaries table or honest empty state", "report-summary" in report_html and ("<table" in report_html or "Деректер табылмады" in html.unescape(report_html)))
            visit(client, "/Reports?" + query({"Type": report_type, "Range": "7", "Search": "not-found-9723", "Sort": "total", "Direction": "asc", "Page": 99999}), role, contains="Деректер табылмады")
            visit(client, "/Reports?" + query({"Type": report_type, "Range": "year", "CategoryId": 1, "GroupBy": "month", "Status": "all"}), role)
            csv_status, _, raw_csv, headers = client.raw("/Reports/Export?" + query({"Type": report_type, "Range": "90"}))
            decoded_csv = raw_csv.decode("utf-8-sig") if raw_csv.startswith(b"\xef\xbb\xbf") else ""
            csv_rows = list(csv.reader(io.StringIO(decoded_csv))) if decoded_csv else []
            record(role + ": " + report_type + " CSV is BOM UTF8 attachment with report data", csv_status == 200 and bool(csv_rows) and "text/csv" in headers.get("Content-Type", "") and "attachment" in headers.get("Content-Disposition", ""), csv_status)
            visit(client, "/Reports/Print?" + query({"Type": report_type, "Range": "90"}), role, contains="басып шығаруға дайын")
            all_assets.update(report_page.assets)
        visit(client, "/Reports?Type=readeractivity", role)
        for path in ["/Analytics/Data?Range=custom&StartDate=2026-10-20&EndDate=2026-10-19", "/Reports/Export?Range=custom&StartDate=2026-10-20&EndDate=2026-10-19", "/Analytics/Data?StartDate=bad", "/Reports?Page=bad"]:
            visit(client, path, role, status=400)
        if role == "Admin":
            for path in ["/Reviews", "/Reviews?Status=visible", "/Reviews?Status=hidden", "/Reviews?Status=deleted", "/Reviews?Search=not-found-4228&Page=9999"]:
                visit(client, path, role)
        else:
            visit(client, "/Reviews", role, status=403)
        visit(client, f"/Reviews/Create?bookId={book_id}", role, status=403)
        visit(client, "/Reviews/Edit/2147483647", role, status=403)
        if role == "Admin":
            post_rejected(client, "/Reviews/Moderate/2147483647", {}, role)
        else:
            _, staff_profile, _ = visit(client, "/Account/Profile", role)
            post_rejected(client, "/Reviews/Moderate/2147483647", {"__RequestVerificationToken": staff_profile.token, "hidden": "true", "rowVersion": "bad"}, role + " moderation forbidden", expected=403)
        post_rejected(client, "/Notifications/ReadAll", {}, role)
        post_rejected(client, "/Notifications/Read/2147483647", {}, role)

    student = Client()
    if not student.login("Student", credentials["Student"]):
        return
    _, _, history_url = visit(student, "/Students/MyHistory", "Student")
    own_match = re.search(r"/Students/Details/(\d+)", history_url, re.I)
    own_id = int(own_match.group(1)) if own_match else None
    record("Student: own identity resolves to database student", bool(own_id))
    rec_source, rec_page, _ = visit(student, "/Recommendations", "Student")
    own_book_ids = baseline.ids(rec_page, "/Books/Details")
    record("Student: real recommendations have six sections and actual book links", len(re.findall(r'class="recommendation-section"', rec_source)) == 6 and bool(own_book_ids))
    scores = [float(value.replace(",", ".")) for value in re.findall(r'class="recommendation-score"><strong>([\d.,]+)<small>%', rec_source)]
    record("Student: displayed recommendation scores stay within 0–100", bool(scores) and all(0 <= value <= 100 for value in scores))
    record("Student: local or enhanced recommendation mode is explicit", "Local Recommendation Engine" in rec_source or "LLM-enhanced explanations" in rec_source)
    signed_link = next((link for link in rec_page.links
        if re.fullmatch(r"/Books/Details/\d+", urllib.parse.urlparse(link).path, re.I)
        and urllib.parse.parse_qs(urllib.parse.urlparse(link).query).get("relevance")), None)
    record("Signed recommendation: real catalog link carries opaque viewer context", bool(signed_link))
    if signed_link:
        parsed_link = urllib.parse.urlparse(signed_link)
        signed_book_id = int(parsed_link.path.rsplit("/", 1)[1])
        token = urllib.parse.parse_qs(parsed_link.query)["relevance"][0]
        recommendation_context(student, signed_link, "current viewer sees computed score on matching book", True, scores[0] if scores else None)
        recommendation_context(anonymous, signed_link, "anonymous viewer cannot reuse student context", False)
        if clients.get("Admin"):
            recommendation_context(clients["Admin"], signed_link, "another authenticated viewer cannot reuse student context", False)
        position = len(token) // 2
        tampered = token[:position] + ("A" if token[position] != "A" else "B") + token[position + 1:]
        recommendation_context(student, parsed_link.path + "?" + query({"relevance": tampered}), "tampered signature silently falls back to public details", False)
        another_book_id = next((value for value in book_ids + own_book_ids if value != signed_book_id), None)
        record("Signed recommendation: different existing book fixture available", another_book_id is not None)
        if another_book_id is not None:
            recommendation_context(student, f"/Books/Details/{another_book_id}?" + query({"relevance": token}), "context cannot be replayed for another existing book", False)
        recommendation_context(student, parsed_link.path, "ordinary public details omit signed recommendation banner", False)
    for recommended_id in own_book_ids[:3]:
        visit(student, f"/Books/Details/{recommended_id}", "Student")
    preferences, preferences_page, _ = visit(student, "/Recommendations/Preferences", "Student")
    record("Student: preferences form is scoped and CSRF protected", bool(preferences_page.token) and preferences_page.hidden.get("StudentId") == str(own_id))
    all_assets.update(rec_page.assets + preferences_page.assets)
    if own_id and preferences_page.token:
        post_rejected(student, "/Recommendations/Preferences", {"StudentId": own_id, "Interests": "x" * 1001, "__RequestVerificationToken": preferences_page.token}, "Student invalid interests", expected=200, contains="1000")
        post_rejected(student, "/Recommendations/Preferences", {"StudentId": own_id, "Interests": "x" * 1001}, "Student missing CSRF")
    foreign_students = [value for value in staff_students if value != own_id]
    record("Student: foreign recommendation fixture exists", bool(foreign_students))
    if foreign_students:
        foreign_id = foreign_students[0]
        for path in [f"/Recommendations?studentId={foreign_id}", f"/Recommendations/Preferences?studentId={foreign_id}"]:
            visit(student, path, "Student IDOR", status=404)
        if preferences_page.token:
            post_rejected(student, "/Recommendations/Preferences", {"StudentId": foreign_id, "Interests": "x" * 1001, "__RequestVerificationToken": preferences_page.token}, "Student foreign preferences", expected=404)
    notification_source, notifications, _ = visit(student, "/Notifications", "Student")
    visit(student, "/Notifications?unreadOnly=true&page=-1", "Student")
    own_notification_ids = [int(match.group(1)) for action in notifications.forms if (match := re.search(r"/Notifications/Read/(\d+)", action, re.I))]
    _, profile, _ = visit(student, "/Account/Profile", "Student")
    if own_notification_ids and clients.get("Admin"):
        admin = clients["Admin"]
        _, admin_profile, _ = visit(admin, "/Account/Profile", "Admin")
        post_rejected(admin, f"/Notifications/Read/{own_notification_ids[0]}", {"__RequestVerificationToken": admin_profile.token}, "Admin foreign notification", expected=404)
    else:
        record("Existing foreign-owned notification fixture available for IDOR test", False)
    if profile.token:
        post_rejected(student, "/Notifications/Read/2147483647", {"__RequestVerificationToken": profile.token}, "Student nonexistent notification", expected=404)
    review_source, review_page, _ = visit(student, f"/Reviews/Create?bookId={book_id}", "Student", contains="Бағаңыз")
    record("Student: review form carries book ID and CSRF", bool(review_page.token) and review_page.hidden.get("BookId") == str(book_id))
    if review_page.token:
        review_id = int(review_page.hidden.get("Id", "0"))
        post_path = f"/Reviews/Edit/{review_id}" if review_id else "/Reviews/Create"
        for invalid_rating in [0, 6]:
            payload = dict(review_page.hidden, Rating=invalid_rating, Comment="HTTP validation only")
            post_rejected(student, post_path, payload, "Student out-of-range rating", expected=200, contains="1–5")
        post_rejected(student, "/Reviews/Create", {"BookId": book_id, "Rating": 0}, "Student review missing CSRF")
    for path in ["/Reviews/Edit/2147483647", "/Reviews/Create?bookId=2147483647"]:
        visit(student, path, "Student", status=404)
    post_rejected(student, "/Reviews/Edit/2147483647", {"__RequestVerificationToken": profile.token, "Id": 2147483647, "BookId": book_id, "Rating": 0}, "Student nonexistent review edit", expected=404)
    for path in ["/Analytics", "/Analytics/Data", "/Reports", "/Reports/Export?Type=loans", "/Reports/Print", "/Copies/Lookup", f"/Copies/Details/{copy_id or 1}", f"/Copies/Qr/{copy_id or 1}", f"/Copies/Label/{copy_id or 1}", "/Reviews"]:
        visit(student, path, "Student role restriction", status=403)
    post_rejected(student, "/Reviews/Moderate/2147483647", {"__RequestVerificationToken": profile.token, "hidden": "true", "rowVersion": "bad"}, "Student moderation forbidden", expected=403)
    all_assets.update(review_page.assets)
    for asset in sorted(all_assets | {"/js/analytics.js", "/js/reports.js", "/js/scanner.js", "/js/catalog.js", "/css/intelligence.css", "/lib/chartjs/chart.umd.js", "/lib/zxing/zxing-browser.min.js"}):
        status, _, raw, _ = anonymous.raw(asset)
        record("Phase 2 static asset: " + asset.split("?")[0], status == 200 and len(raw) > 0, status)


if __name__ == "__main__":
    try:
        run()
    except Exception as error:
        record("Phase 2 HTTP runner completed", False, detail=type(error).__name__)
    passed = sum(result["passed"] for result in RESULTS)
    failed = len(RESULTS) - passed
    report = {"base_url": BASE, "passed": passed, "failed": failed, "checks": RESULTS,
              "scope": "GET, authentication, and deliberately rejected POSTs only; no successful application-data writes"}
    (ROOT / "tests" / "phase2_http_smoke.results.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"PHASE 2 HTTP: {passed} passed, {failed} failed", flush=True)
    sys.exit(1 if failed else 0)
