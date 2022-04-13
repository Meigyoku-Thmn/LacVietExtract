(function () {
    var currentScript = document.scripts[document.scripts.length - 1];
    var guid = currentScript.dataset.guid;
    var url = document.getElementById(guid + '-img').src;
    var script = document.createElement('script');
    script.src = url;
    script.dataset.guid = guid;
    currentScript.parentNode.appendChild(script);
})()