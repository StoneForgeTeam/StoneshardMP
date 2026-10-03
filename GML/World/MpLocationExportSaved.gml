/// @stoneforge return string
/// @stoneforge param saver GmValue
// The location an o_roomEntitySaver has just saved (the end of its user event 2), as MpLocationExport.
function MpLocationExportSaved(saver)
{
    if (!instance_exists(saver))
        return "";
    var _location = saver.locationTag;
    var _room = saver.roomTag;
    var _preset = saver.presetTag;
    return MpLocationExport(_location, _room, _preset);
}
