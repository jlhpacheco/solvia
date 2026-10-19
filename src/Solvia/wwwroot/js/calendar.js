// Native term-dates list (the calendar seam's SHIPPED renderer) —
// renders the TermDates JSON island on the dates page as a plain
// list. Bryntum Calendar is NOT embedded: it has no free community
// edition (paid license or 45-day trial only, and a trial would
// expire during judging Dec 1–15, 2026). A licensed Bryntum build
// would consume this exact same JSON shape via a drop-in
// calendar.bryntum.js + config Calendar:Renderer=bryntum — see
// docs/SPONSORS.md. (/College/DatesJson serves the JSON directly.)
(function () {
    var dataEl = document.getElementById('term-dates-data');
    var host = document.getElementById('term-calendar');
    if (!dataEl || !host) {
        return;
    }

    var dates = JSON.parse(dataEl.textContent);

    function big(iso) {
        var d = new Date(iso + 'T00:00:00');
        return d.toLocaleDateString('en-US', {
            weekday: 'long', year: 'numeric', month: 'long', day: 'numeric'
        });
    }

    var items = [
        ['Term starts', dates.termStart],
        ['Term ends', dates.termEnd],
        ['Permit ends', dates.permitEndDate],
        ['Renew by', dates.renewalDate]
    ];

    var list = document.createElement('ul');
    list.className = 'list-group fs-4 mt-4';
    items.forEach(function (item) {
        var li = document.createElement('li');
        li.className = 'list-group-item';
        var strong = document.createElement('strong');
        strong.textContent = item[0] + ': ';
        li.appendChild(strong);
        li.appendChild(document.createTextNode(big(item[1])));
        list.appendChild(li);
    });

    host.appendChild(list);
})();
