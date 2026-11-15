import QtQuick
import QtQuick.Layouts

Item {
    id: meter

    property string label
    property string count
    property real fraction: 0
    property bool done: false
    property bool clickable: false
    property color tint: theme.accent

    signal clicked()

    implicitHeight: body.implicitHeight + 8

    Rectangle {
        anchors.fill: parent
        anchors.leftMargin: -6
        anchors.rightMargin: -6
        radius: 6
        visible: meter.clickable && hover.hovered
        color: theme.alpha(theme.accent, 0.08)
    }

    ColumnLayout {
        id: body
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.verticalCenter: parent.verticalCenter
        spacing: 4

        RowLayout {
            Layout.fillWidth: true
            spacing: 6

            Text {
                Layout.fillWidth: true
                elide: Text.ElideRight
                text: meter.label
                color: theme.text
                font.family: theme.font
                font.pixelSize: 13
            }

            Text {
                text: (meter.done ? "✓ " : "") + meter.count
                color: meter.done ? theme.success : theme.dim
                font.family: theme.font
                font.pixelSize: 12
            }

            Text {
                visible: meter.clickable
                text: "›"
                color: theme.accent
                font.family: theme.font
                font.pixelSize: 15
            }
        }

        Rectangle {
            Layout.fillWidth: true
            implicitHeight: 6
            radius: 3
            color: theme.bgDeep
            border.width: 1
            border.color: theme.alpha(meter.tint, 0.25)

            Rectangle {
                width: meter.fraction > 0 ? Math.max(height, parent.width * meter.fraction) : 0
                height: parent.height
                radius: 3
                color: meter.done ? theme.success : theme.alpha(meter.tint, 0.85)
            }
        }
    }

    HoverHandler {
        id: hover
        cursorShape: meter.clickable ? Qt.PointingHandCursor : Qt.ArrowCursor
    }

    TapHandler {
        enabled: meter.clickable
        onTapped: meter.clicked()
    }
}
