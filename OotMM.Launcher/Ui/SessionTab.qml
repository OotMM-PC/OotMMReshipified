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
        tip: "Opens the running game's own settings menu (graphics, audio, input). Esc in the game does the same."
        onClicked: tab.vm.openGameMenu()
    }

    GoldButton {
        Layout.fillWidth: true
        text: "Restart active game"
        enabled: !!(tab.vm && tab.vm.coordinatorReady)
        tip: "Closes and relaunches the active game from its last save."
        onClicked: tab.vm.restartGame()
    }

    GoldButton {
        Layout.fillWidth: true
        text: "Restart to OoT title screen"
        enabled: !!(tab.vm && tab.vm.coordinatorReady)
        tip: "Closes the active game and boots Ocarina of Time to its title screen, ready to pick your save."
        onClicked: tab.vm.restartToOotTitle()
    }

    GoldButton {
        id: toMm
        Layout.fillWidth: true
        visible: !!(tab.vm && tab.vm.showSwitchToMm)
        text: "Switch to Majora's Mask…"
        tint: theme.mm
        labelColor: "#E08A7A"
        tip: "Dev mode: forcing a game switch outside a cross-game load zone may break logic. Pick which age Link arrives as in MM."
        onClicked: arrival.popup(toMm, 0, toMm.height)

        Menu {
            id: arrival

            padding: 4

            background: Rectangle {
                implicitWidth: 228
                color: theme.raised
                radius: 6
                border.width: 1
                border.color: theme.alpha(theme.mm, 0.6)
            }

            GoldMenuItem {
                text: "Arrive as child Link"
                onTriggered: tab.vm.switchGameAsChild()
            }

            GoldMenuItem {
                text: "Arrive as adult Link"
                onTriggered: tab.vm.switchGameAsAdult()
            }
        }
    }

    GoldButton {
        Layout.fillWidth: true
        visible: !!(tab.vm && tab.vm.showSwitchToOot)
        text: "Switch to Ocarina of Time"
        tint: theme.gold
        labelColor: "#E08A7A"
        tip: "Dev mode: forcing a game switch outside a cross-game load zone may break logic. OoT resumes at its save's age."
        onClicked: tab.vm.switchGame()
    }

    GoldButton {
        Layout.fillWidth: true
        text: "End session"
        enabled: !!(tab.vm && tab.vm.coordinatorReady)
        tip: "Saves are kept, so launching this seed again resumes where you left off."
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
                on: tab.vm ? tab.vm.speedSmooth : false
                ToolTip.visible: hovered
                ToolTip.delay: 500
                ToolTip.text: "Always renders at the graphics FPS, interpolating slow game updates and skipping renders between fast ones."
                onFlipped: value => tab.vm.speedSmooth = value
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
        tip: "Permanently deletes ALL progress for this seed: both games' saves, the shared ledger and the check log. This cannot be undone."
        onClicked: tab.vm.resetProgress()
    }

    Item {
        Layout.fillHeight: true
    }
}
