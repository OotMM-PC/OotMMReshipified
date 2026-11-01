import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts

ColumnLayout {
    id: tab

    property var vm

    spacing: 6

    Text {
        Layout.fillWidth: true
        visible: !!(tab.vm && tab.vm.isEmpty)
        wrapMode: Text.WordWrap
        text: "Nothing yet. Found checks and received items appear here."
        color: theme.dim
        font.family: theme.font
        font.pixelSize: 13
    }

    ListView {
        id: list
        Layout.fillWidth: true
        Layout.fillHeight: true
        clip: true
        model: tab.vm ? tab.vm.entries : []
        ScrollBar.vertical: ScrollBar { }
        property bool follow: true
        onContentYChanged: follow = atYEnd || contentHeight <= height
        onCountChanged: if (follow) Qt.callLater(positionViewAtEnd)

        delegate: Item {
            id: row
            required property var modelData
            width: list.width - 10
            height: line.implicitHeight + 10

            RowLayout {
                id: line
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.verticalCenter: parent.verticalCenter
                spacing: 8

                Text {
                    Layout.alignment: Qt.AlignTop
                    text: row.modelData.time
                    color: theme.faint
                    font.family: theme.font
                    font.pixelSize: 12
                }

                Text {
                    Layout.fillWidth: true
                    wrapMode: Text.WordWrap
                    text: row.modelData.message
                    textFormat: Text.PlainText
                    color: row.modelData.isRemote ? theme.warning : theme.text
                    font.family: theme.font
                    font.pixelSize: 13
                }
            }

            Rectangle {
                anchors.bottom: parent.bottom
                width: parent.width
                height: 1
                color: theme.alpha(theme.accent, 0.1)
            }
        }
    }
}
