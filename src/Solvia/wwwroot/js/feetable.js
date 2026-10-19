// Native fee-table renderer (the fallback) — plain HTML table, no
// libraries. renderFeeTable(el, rows) draws the same rows the AG Grid
// adapter draws, from the same JSON payload (see _FeeTable.cshtml).
// Also used by feetable.aggrid.js when the AG Grid CDN is unreachable.
function renderFeeTable(el, rows) {
    var table = document.createElement('table');
    table.className = 'table table-striped';

    var head = document.createElement('thead');
    var headRow = document.createElement('tr');
    ['Campus', 'Semester price', 'Day-pass price', 'Source note'].forEach(function (label) {
        var th = document.createElement('th');
        th.scope = 'col';
        th.textContent = label;
        headRow.appendChild(th);
    });
    head.appendChild(headRow);
    table.appendChild(head);

    var body = document.createElement('tbody');
    rows.forEach(function (row) {
        var tr = document.createElement('tr');
        [row.campus, row.semesterPrice, row.dayPass, row.sourceNote].forEach(function (value) {
            var td = document.createElement('td');
            td.textContent = value == null ? '' : value;
            tr.appendChild(td);
        });
        body.appendChild(tr);
    });
    table.appendChild(body);

    el.innerHTML = '';
    el.appendChild(table);
}

window.renderFeeTable = renderFeeTable;

// The same search stays available if the optional grid CDN is offline.
window.bindNativeFeeSearch = function (table) {
    var input = document.getElementById('fee-search');
    var status = document.getElementById('fee-results');
    if (!table || !input || !status) { return; }
    input.addEventListener('input', function () {
        var terms = input.value.toLocaleLowerCase().trim().split(/\s+/);
        var visible = 0;
        var rows = table.tBodies[0].rows;
        Array.from(rows).forEach(function (row) {
            var text = row.textContent.toLocaleLowerCase();
            row.hidden = !terms.every(function (term) { return text.includes(term); });
            if (!row.hidden) { visible++; }
        });
        status.textContent = visible + ' of ' + rows.length + ' campuses';
    });
};
