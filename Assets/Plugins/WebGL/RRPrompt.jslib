mergeInto(LibraryManager.library, {
  // Mobile browsers have no keyboard for Unity's IMGUI text fields, so ask with the browser's own prompt instead.
  RR_Prompt: function (msg, def) {
    var d = UTF8ToString(def);
    var r = window.prompt(UTF8ToString(msg), d);
    if (r === null || r === undefined) r = d;
    var len = lengthBytesUTF8(r) + 1;
    var buf = _malloc(len);
    stringToUTF8(r, buf, len);
    return buf;
  }
});
