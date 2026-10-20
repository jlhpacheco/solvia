// Read-only progressive enhancement. The server-rendered table/details are
// complete without JavaScript or the CDN. No financial requests are made.
(function () {
    'use strict';
    var grid = document.getElementById('payment-review-grid');
    var nativeTable = document.getElementById('payment-review-native');
    var data = document.getElementById('payment-review-data');
    var library = document.getElementById('payment-review-library');
    var started = false;

    function openTarget() {
        var id = window.location.hash.slice(1);
        if (!/^record-[a-f0-9]{32}$/.test(id)) return;
        var detail = document.getElementById(id);
        if (detail instanceof HTMLDetailsElement) detail.open = true;
    }
    openTarget();
    window.addEventListener('hashchange', openTarget);

    function startGrid() {
        if (started || !grid || !nativeTable || !data || !window.agGrid ||
            typeof window.agGrid.createGrid !== 'function') return;
        started = true;
        try {
            var rows = JSON.parse(data.textContent);
            // Node renderers use textContent, never concatenate stored values
            // into HTML. Only server-produced GUID anchors are navigable.
            function recordLink(parameters) {
                var link = document.createElement('a');
                link.href = '#' + parameters.data.detailsId;
                link.textContent = parameters.value;
                return link;
            }
            grid.hidden = false;
            window.agGrid.createGrid(grid, {
                theme: window.agGrid.themeQuartz.withParams({
                    accentColor: '#007f79', fontFamily: 'inherit', fontSize: 14,
                    headerBackgroundColor: '#edf4f8', headerTextColor: '#071b46',
                    borderColor: '#dce4ed'
                }),
                rowData: rows,
                rowHeight: 66,
                headerHeight: 46,
                suppressCellFocus: false,
                ensureDomOrder: true,
                onFirstDataRendered: function () { nativeTable.hidden = true; },
                defaultColDef: { sortable: true, resizable: true, wrapText: true, autoHeight: true },
                columnDefs: [
                    { field: 'reference', headerName: 'Record', cellRenderer: recordLink, minWidth: 200, flex: 1.5 },
                    { field: 'campus', headerName: 'Campus', minWidth: 220, flex: 2 },
                    { field: 'category', headerName: 'Category', minWidth: 115, flex: 1 },
                    { field: 'paymentStatus', headerName: 'Payment', minWidth: 150, flex: 1, valueFormatter: function (p) { return p.value === 'NotRequired' ? 'No payment required' : p.value; } },
                    { field: 'priceCents', headerName: 'Amount (USD)', minWidth: 140, flex: 1, cellDataType: 'number',
                        // An unpriced draft sorts as missing, never as a free $0.
                        valueGetter: function (p) { return p.data && p.data.hasConfirmedAmount ? p.data.priceCents : null; },
                        valueFormatter: function (p) { return p.data ? p.data.amount : 'Not priced'; } },
                    { field: 'isActive', headerName: 'Permit', minWidth: 110, flex: 1, cellDataType: false,
                        valueFormatter: function (p) { return p.value ? 'Active' : 'Inactive'; } }
                ]
            });
        } catch (_error) {
            // A render failure must never remove the usable original table.
            grid.hidden = true;
            nativeTable.hidden = false;
        }
    }
    if (library) library.addEventListener('load', startGrid, { once: true });
    startGrid(); // Covers an already loaded async library without a race.
})();
