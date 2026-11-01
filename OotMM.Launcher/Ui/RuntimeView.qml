import QtQuick
import QtQuick.Layouts
import QtQuick.Window

Item {
    id: view

    property var vm

    Rectangle {
        anchors.fill: parent
        color: theme.bgDeep
    }

    RowLayout {
        anchors.fill: parent
        spacing: 0

        Item {
            id: gameArea
            Layout.fillWidth: true
            Layout.fillHeight: true
            Layout.margins: 10

            function report() {
                if (!view.vm || view.vm.isPreview || width < 1 || height < 1)
                    return
                var p = gameArea.mapToItem(null, 0, 0)
                app.setGameArea(p.x, p.y, width, height, Screen.devicePixelRatio)
            }

            onXChanged: report()
            onYChanged: report()
            onWidthChanged: report()
            onHeightChanged: report()
            Component.onCompleted: Qt.callLater(report)

            Rectangle {
                anchors.fill: parent
                radius: 10
                color: view.vm && view.vm.isPreview ? theme.panel : "#000000"
            }

            Column {
                anchors.centerIn: parent
                visible: !!(view.vm && view.vm.isPreview)
                spacing: 12

                PixelIcon {
                    anchors.horizontalCenter: parent.horizontalCenter
                    name: view.vm && view.vm.activeIsOot ? "OcarinaOfTime" : "FierceDeityMask"
                    width: 64
                    height: 64
                }

                Text {
                    anchors.horizontalCenter: parent.horizontalCenter
                    text: "The game window docks here once it starts."
                    color: theme.dim
                    font.family: theme.font
                    font.pixelSize: 13
                }

                LinkButton {
                    anchors.horizontalCenter: parent.horizontalCenter
                    text: "Back to configuration"
                    onClicked: view.vm.backToConfig()
                }
            }

            Text {
                x: 14
                y: 10
                visible: !!(view.vm && view.vm.isPreview)
                text: "Layout preview · no live session"
                color: theme.warning
                font.family: theme.font
                font.pixelSize: 12
            }
        }

        Sidebar {
            Layout.fillHeight: true
            vm: view.vm
        }
    }
}
