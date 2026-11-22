// The only script on the page: the "Copy" buttons next to things you paste into the game.
(function () {
    'use strict';

    function select(field) {
        field.focus();
        field.select();
    }

    function flash(button, text) {
        var original = button.getAttribute('data-label') || button.textContent;
        button.setAttribute('data-label', original);
        button.textContent = text;
        window.setTimeout(function () {
            button.textContent = original;
        }, 1600);
    }

    document.querySelectorAll('[data-copy]').forEach(function (button) {
        var field = document.getElementById(button.getAttribute('data-copy'));
        if (!field)
            return;

        field.addEventListener('focus', function () {
            field.select();
        });

        button.addEventListener('click', function () {
            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(field.value).then(function () {
                    flash(button, 'Copied');
                }, function () {
                    select(field);
                    flash(button, 'Press Ctrl+C');
                });
                return;
            }

            select(field);
            flash(button, 'Press Ctrl+C');
        });
    });
})();
