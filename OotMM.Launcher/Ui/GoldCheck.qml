import QtQuick
import QtQuick.Controls.Basic

AbstractButton {
    id: control

    property color tint: theme.accent
    property bool on: false
    property bool wrap: true

    signal flipped(bool value)

    font.family: theme.font
    font.pixelSize: 13
    spacing: 8
    padding: 2
    hoverEnabled: true
    focusPolicy: Qt.StrongFocus
    implicitWidth: implicitContentWidth + leftPadding + rightPadding
    implicitHeight: Math.max(implicitContentHeight, box.height) + topPadding + bottomPadding

    Accessible.role: Accessible.CheckBox
    Accessible.checkable: true
    Accessible.checked: on

    onClicked: flipped(!on)

    Rectangle {
        id: box
        x: control.leftPadding
        y: control.topPadding + (control.availableHeight - height) / 2
        width: 16
        height: 16
        radius: 3
        color: control.on ? control.tint : theme.field
        border.width: 1
        border.color: control.enabled ? (control.hovered || control.on || control.visualFocus ? control.tint : theme.alpha(control.tint, 0.45))
                                      : theme.alpha(control.tint, 0.15)
        opacity: control.enabled ? 1 : 0.5

        Text {
            anchors.centerIn: parent
            text: "✓"
            visible: control.on
            color: theme.goldInk
            font.pixelSize: 12
            font.bold: true
        }
    }

    contentItem: Text {
        leftPadding: box.width + (control.text.length > 0 ? control.spacing : 0)
        text: control.text
        font: control.font
        color: control.enabled ? theme.text : theme.faint
        verticalAlignment: Text.AlignVCenter
        wrapMode: control.wrap ? Text.WordWrap : Text.NoWrap
        elide: control.wrap ? Text.ElideNone : Text.ElideRight
    }
}
