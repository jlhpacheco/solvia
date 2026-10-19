// AG Grid adapter — the ONLY AG Grid integration point in Solvia.
//
// Edition: AG Grid Community (free edition, loaded from the official
// jsDelivr CDN by _FeeTable.cshtml). Community needs no license key,
// and Solvia never uses a trial license — a 45-day trial would expire
// in the middle of judging. If the CDN cannot be reached, this adapter
// falls back to the native renderer (feetable.js) with the same rows.
(function () {
    var dataEl = document.getElementById('fee-table-data');
    var gridEl = document.getElementById('feeGrid');
    if (!dataEl || !gridEl) {
        return;
    }

    var rows = JSON.parse(dataEl.textContent);

    if (typeof agGrid === 'undefined' || typeof agGrid.createGrid !== 'function') {
        if (typeof window.renderFeeTable === 'function') {
            window.renderFeeTable(gridEl, rows);
            window.bindNativeFeeSearch(gridEl.querySelector('table'));
        }
        return;
    }

    // Sort prices by their real cent value; "not published" (null)
    // always sorts last, and FREE (0) sorts as the real $0 it is.
    function byCents(_valueA, _valueB, nodeA, nodeB, descending) {
        var a = nodeA && nodeA.data ? nodeA.data.semesterPriceCents : null;
        var b = nodeB && nodeB.data ? nodeB.data.semesterPriceCents : null;
        if (a === null && b === null) { return 0; }
        if (a === null) { return descending ? -1 : 1; }
        if (b === null) { return descending ? 1 : -1; }
        return a - b;
    }

    var gridOptions = {
        theme: agGrid.themeQuartz.withParams({
            accentColor: '#007f79',
            fontFamily: 'inherit',
            fontSize: 14,
            headerBackgroundColor: '#edf4f8',
            headerTextColor: '#071b46',
            borderColor: '#dce4ed'
        }),
        rowHeight: 56,
        headerHeight: 48,
        columnDefs: [
            { field: 'campus', headerName: 'Campus', flex: 2, minWidth: 230 },
            { field: 'semesterPrice', headerName: 'Semester price', minWidth: 165, flex: 1, comparator: byCents },
            { field: 'dayPass', headerName: 'Day-pass price', minWidth: 220, flex: 2 },
            { field: 'sourceNote', headerName: 'Source note', minWidth: 280, flex: 2 }
        ],
        rowData: rows,
        defaultColDef: { sortable: true, filter: true, resizable: true, wrapText: true, autoHeight: true },
        onFilterChanged: updateCount,
        onGridReady: updateCount
    };

    function updateCount(event) {
        document.getElementById('fee-results').textContent = event.api.getDisplayedRowCount() + ' of ' + rows.length + ' campuses';
    }
    var api = agGrid.createGrid(gridEl, gridOptions);
    document.getElementById('fee-search').addEventListener('input', function (event) {
        api.setGridOption('quickFilterText', event.target.value);
    });
})();
