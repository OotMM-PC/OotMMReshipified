import QtQuick
import QtQuick.Controls.Basic

TextField {
    id: control

    property color tint: theme.accent

    implicitHeight: 30
    leftPadding: 9
    rightPadding: 9
    font.family: theme.font
    font.pixelSize: 13
    color: theme.text
    placeholderTextColor: theme.faint
    selectByMouse: true
    selectionColor: theme.alpha(tint, 0.45)
    selectedTextColor: theme.text
    onPressed: app.setTextFieldFocus(true)
    onActiveFocusChanged: app.setTextFieldFocus(activeFocus)

    background: Rectangle {
        radius: 5
        color: theme.field
        border.width: 1
        border.color: control.activeFocus ? control.tint : theme.alpha(control.tint, 0.35)
    }
}
