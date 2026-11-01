import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts

ColumnLayout {
    id: tab

    property var vm

    spacing: 8

    Text {
        Layout.fillWidth: true
        visible: !!(tab.vm && tab.vm.showUnavailableNote)
        wrapMode: Text.WordWrap
        text: tab.vm ? tab.vm.unavailableNote : ""
        color: theme.dim
        font.family: theme.font
        font.pixelSize: 12
    }

    GoldButton {
        Layout.fillWidth: true
        text: "Open the game's menu"
        enabled: !!(tab.vm && tab.vm.coordinatorReady)
        onClicked: tab.vm.openGameMenu()
    }

    GoldButton {
        Layout.fillWidth: true
        text: "Restart active game"
        enabled: !!(tab.vm && tab.vm.coordinatorReady)
        onClicked: tab.vm.restartGame()
    }

    GoldButton {
        Layout.fillWidth: true
        visible: !!(tab.vm && tab.vm.showSwitchToMm)
        text: "Switch to Majora's Mask"
        tint: theme.mm
        onClicked: tab.vm.switchGame()
    }

    RowLayout {
        Layout.fillWidth: true
        visible: !!(tab.vm && tab.vm.showSwitchToOot)
        spacing: 6

        GoldButton {
            Layout.fillWidth: true
            text: "To OoT as child"
            tint: theme.gold
            onClicked: tab.vm.switchGameAsChild()
        }

        GoldButton {
            Layout.fillWidth: true
            text: "To OoT as adult"
            tint: theme.gold
            onClicked: tab.vm.switchGameAsAdult()
        }
    }

    GoldButton {
        Layout.fillWidth: true
        text: "End session"
        enabled: !!(tab.vm && tab.vm.coordinatorReady)
        onClicked: tab.vm.endSession()
    }

    Rectangle {
        Layout.fillWidth: true
        Layout.topMargin: 4
        visible: !!(tab.vm && tab.vm.showSpeed)
        implicitHeight: speed.implicitHeight + 20
        radius: 6
        color: theme.raised

        ColumnLayout {
            id: speed
            anchors.fill: parent
            anchors.margins: 10
            spacing: 6

            RowLayout {
                Layout.fillWidth: true

                Text {
                    Layout.fillWidth: true
                    text: "Game speed"
                    color: theme.text
                    font.family: theme.font
                    font.pixelSize: 13
                }

                LinkButton {
                    visible: !!(tab.vm && tab.vm.showSpeedReset)
                    text: "Reset"
                    font.pixelSize: 12
                    onClicked: tab.vm.resetSpeed()
                }

                Text {
                    text: tab.vm ? tab.vm.speedText : ""
                    color: theme.dim
                    font.family: theme.font
                    font.pixelSize: 13
                }
            }

            GoldSlider {
                Layout.fillWidth: true
                from: tab.vm ? tab.vm.speedMin : 0
                to: tab.vm ? tab.vm.speedMax : 250
                stepSize: 5
                snapMode: Slider.SnapAlways
                value: tab.vm ? tab.vm.speedPercent : 100
                onMoved: tab.vm.speedPercent = value
            }

            GoldCheck {
                Layout.fillWidth: true
                text: "Keep render FPS fixed while changing game speed"
                checked: tab.vm ? tab.vm.speedSmooth : false
                onToggled: tab.vm.speedSmooth = checked
            }
        }
    }

    GoldButton {
        Layout.fillWidth: true
        Layout.topMargin: 4
        visible: !!(tab.vm && tab.vm.showReset)
        text: tab.vm ? tab.vm.resetButtonText : ""
        danger: true
        enabled: !!(tab.vm && tab.vm.coordinatorReady)
        onClicked: tab.vm.resetProgress()
    }

    Item {
        Layout.fillHeight: true
    }
}
