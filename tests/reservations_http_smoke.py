"""Read-only reservation routes/roles plus deliberately invalid POSTs on the seeded app.

Run: python tests/reservations_http_smoke.py http://localhost:5298
Successful workflows belong to reservations_ui_smoke.cjs on disposable port 5399.
"""
from __future__ import annotations

import json
import re
from pathlib import Path
from urllib.parse import urlencode, urlparse

import http_smoke as baseline
from phase2_http_smoke import Client, Page

ROOT = Path(__file__).resolve().parents[1]
RESULTS = []
baseline.RESULTS = RESULTS
record = baseline.record


def denied(client, route, label, data=None):
    status, url, _ = client.request(route, data)
    record(label, status == 403 or urlparse(url).path == "/Account/AccessDenied", status)


def run():
    config = json.loads((ROOT / "appsettings.Local.json").read_text(encoding="utf-8-sig"))
    public = Client()
    for route in ["/Reservations", "/Reservations/Create?bookId=1", "/Reservations/Details/2147483647", "/Reservations/Qr/2147483647", "/Reservations/Lookup", "/Reservations/Slots?pickupDate=2026-10-26&pickupLocationId=main"]:
        status, url, _ = public.request(route)
        record("Anonymous reservation route requires authentication: " + route.split("?")[0], status == 200 and urlparse(url).path == "/Account/Login", status)

    for role in ["Student", "Librarian", "Admin"]:
        client = Client()
        if not client.login(role, config["SeedUsers"][role]):
            continue
        for query in ["", "?Status=Pending", "?Status=ReadyForPickup", "?Status=Queued", "?Status=Fulfilled", "?Status=Cancelled", "?Status=Expired", "?Status=all", "?Status=malformed&Page=-12", "?Search=edulibrary_reservation_no_match&Page=2147483647"]:
            status, url, source = client.request("/Reservations" + query)
            record(role + " reservation list/filter " + (query or "active"), status == 200 and Page(source).lang == "kk" and 'id="reservations-index"' in source, status)
        status, _, source = client.request("/Reservations")
        token = Page(source).token
        # The layout logout form supplies the same identity-bound antiforgery token.
        record(role + " reservation page has antiforgery token", bool(token), status)
        for route in ["/Reservations/Details/2147483647", "/Reservations/Qr/2147483647"]:
            status, _, _ = client.request(route)
            record(role + " missing reservation is private 404: " + route, status == 404, status)
        for route in ["/Reservations/Create", "/Reservations/MarkReady", "/Reservations/Fulfill", "/Reservations/Cancel/2147483647", "/Reservations/Lookup"]:
            status, url, _ = client.request(route, {"Id": "2147483647"})
            # Role authorization can deny before antiforgery. Neither may execute content writes.
            record(role + " missing-CSRF reservation POST rejected: " + route, status in (400, 403) or urlparse(url).path == "/Account/AccessDenied", status)
        for params in [{}, {"pickupDate": "malformed", "pickupLocationId": "main"}, {"pickupDate": "2026-10-26"}]:
            status, _, raw, headers = client.raw("/Reservations/Slots?" + urlencode(params))
            record(role + " incomplete/malformed slots reject as JSON", status == 400 and "json" in headers.get("Content-Type", ""), status)
        status, _, raw, _ = client.raw("/Reservations/Slots?pickupDate=1900-01-01&pickupLocationId=main")
        record(role + " past date offers no slots", status == 200 and json.loads(raw) == [], status)
        status, _, raw, _ = client.raw("/Reservations/Slots?pickupDate=2026-10-26&pickupLocationId=foreign")
        record(role + " unknown pickup location offers no slots", status == 200 and json.loads(raw) == [], status)
        if role == "Student":
            for route in ["/Reservations/Lookup", "/Reservations/Prepare/2147483647"]:
                denied(client, route, "Student cannot open staff reservation tool: " + route)
            for route in ["/Reservations/MarkReady", "/Reservations/Fulfill", "/Reservations/Lookup"]:
                denied(client, route, "Student valid-CSRF POST cannot invoke staff tool: " + route, {"__RequestVerificationToken": token, "Id": "2147483647"})
            status, _, source = client.request("/Books")
            book = next((int(match[1]) for link in Page(source).links if (match := re.match(r"/Books/Details/(\d+)", link))), None)
            record("Student catalog exposes real books for reservation", book is not None)
            status, _, source = client.request(f"/Reservations/Create?bookId={book}")
            form = Page(source)
            record("Student real reservation form has protected POST and location", status == 200 and 'id="reservation-create-form"' in source and bool(form.token) and "PickupLocationId" in form.options, status)
            for asset in [asset for asset in form.assets if "/reservations." in asset]:
                actual, _, _ = client.request(asset)
                record("Reservation frontend asset responds", actual == 200, actual)
            status, _, _ = client.request("/Reservations/Create", {"BookId": "0", "JoinQueue": "false", "PickupLocationId": "main", "__RequestVerificationToken": form.token})
            record("Invalid reservation book cannot persist", status == 404, status)
        else:
            denied(client, "/Reservations/Create?bookId=1", role + " cannot create a student reservation as staff")
            status, _, source = client.request("/Reservations/Lookup")
            lookup = Page(source)
            record(role + " staff code lookup is protected POST with scanner", status == 200 and 'data-reservation-lookup-form' in source and bool(lookup.token) and 'id="copy-scanner-start"' in source, status)
            status, url, source = client.request("/Reservations/Lookup", {"Code": "ELR-invalid", "__RequestVerificationToken": lookup.token})
            record(role + " malformed pickup code displays validation without resolving reservation", status == 200 and urlparse(url).path == "/Reservations/Lookup" and 'field-validation-error' in source, status)


if __name__ == "__main__":
    run()
    report = {"passed": sum(r["passed"] for r in RESULTS), "failed": sum(not r["passed"] for r in RESULTS), "checks": RESULTS}
    target = ROOT / "artifacts" / "reservation-http-results.json"
    target.parent.mkdir(exist_ok=True)
    target.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Reservation HTTP: {report['passed']} passed, {report['failed']} failed.")
    raise SystemExit(bool(report["failed"]))
