(function () {
    if (!String.prototype.includes) {
        String.prototype.includes = function (search, start) {
            'use strict';
            if (search instanceof RegExp)
                throw TypeError('first argument must not be a RegExp');
            if (start === undefined) { start = 0; }
            return this.indexOf(search, start) !== -1;
        };
    }

    var guid = document.scripts[document.scripts.length - 1].dataset.guid;

    window.addEventListener("load", function () {
        var baseElem = document.getElementById(guid);
        var answerShown = false;

        var textInputElems = [].slice.call(baseElem.getElementsByTagName("input"))
            .filter(function (e) { return e.type == 'text'; });
        if (window.navigator.userAgent.includes("GoldenDict")) {
            textInputElems.forEach(function (e) {
                e.addEventListener("keydown", function (event) {
                    if (answerShown)
                        return;
                    if (event.key != "Enter" && event.keyCode != 13 && event.key != "Space" && event.keyCode != 32)
                        return;
                    var answer = prompt("Type your answer:", e.value);
                    if (answer != null)
                        e.value = answer.trim();
                    event.preventDefault();
                });
            });
        }

        var btnShowAnswers = [].slice.call(baseElem.getElementsByClassName("show-answer-btn"));
        btnShowAnswers.forEach(function (e) {
            e.addEventListener("click", function () {
                baseElem.classList.add("show-answer");
                btnShowAnswers.forEach(function (e) { e.parentNode.removeChild(e); });
                textInputElems.forEach(function (e) { e.readOnly = true; });
                answerShown = true;
            });
        });

        var choiceElems = [].slice.call(baseElem.getElementsByTagName("d-choice"));
        var choiceGroups = choiceElems.reduce(function (acc, e) {
            var name = e.getAttribute("name");
            if (name == null)
                return acc;
            var group = acc[name];
            if (group == null)
                acc[name] = group = [];
            group.push(e);
            return acc;
        }, {});
        Object.keys(choiceGroups).forEach(function (name) {
            var group = choiceGroups[name];
            group.forEach(function (e) {
                e.addEventListener("click", function () {
                    if (answerShown)
                        return;
                    group.forEach(function (ce) {
                        if (ce != e)
                            ce.classList.remove('selected');
                        else
                            ce.classList.add('selected');
                    });
                });
            });
        });
    });
})()