// Native fee-table renderer (T6 fallback) — plain HTML table, no
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
