import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts

Item {
    id: view

    property var vm

    Flickable {
        anchors.fill: parent
        contentHeight: column.implicitHeight + 56
        clip: true
        boundsBehavior: Flickable.StopAtBounds
        ScrollBar.vertical: ScrollBar { }

        ColumnLayout {
            id: column
            width: Math.min(view.width - 48, 1040)
            x: (view.width - width) / 2
            y: Math.max(28, (view.height - implicitHeight) / 2)
            spacing: 14

            BracketFrame {
                Layout.fillWidth: true
                implicitHeight: 112
                fill: "#1C150B"
                padding: 20

                RowLayout {
                    anchors.fill: parent
                    spacing: 18

                    Triforce {
                        Layout.preferredWidth: 52
                        Layout.preferredHeight: 46
                    }

                    ColumnLayout {
                        Layout.fillWidth: true
                        spacing: 2

                        Text {
                            text: "OoTMM PC"
                            color: theme.accentBright
                            font.family: theme.font
                            font.pixelSize: 34
                            font.weight: Font.DemiBold
                        }

                        Text {
                            text: "OCARINA OF TIME  ×  MAJORA'S MASK"
                            color: theme.dim
                            font.family: theme.font
                            font.pixelSize: 12
                            font.letterSpacing: 3
                        }
                    }

                    ColumnLayout {
                        Layout.alignment: Qt.AlignRight | Qt.AlignVCenter
                        spacing: 1

                        Text {
                            Layout.alignment: Qt.AlignRight
                            text: "Quest seed"
                            color: theme.dim
                            font.family: theme.font
                            font.pixelSize: 12
                        }

                        Text {
                            Layout.alignment: Qt.AlignRight
                            text: view.vm.hasSeed ? view.vm.seedId : "No seed chosen"
                            color: view.vm.hasSeed ? theme.success : theme.faint
                            font.family: view.vm.hasSeed ? theme.mono : theme.font
                            font.pixelSize: 22

                            ToolTip.visible: seedHover.hovered && view.vm.seedPath.length > 0
                            ToolTip.text: view.vm.seedPath

                            HoverHandler {
                                id: seedHover
                            }
                        }

                        Row {
                            Layout.alignment: Qt.AlignRight
                            spacing: 6

                            Text {
                                visible: view.vm.hasSeed
                                text: view.vm.seedDescription + "  ·"
                                color: theme.dim
                                font.family: theme.font
                                font.pixelSize: 13
                            }

                            LinkButton {
                                text: view.vm.hasSeed ? "change" : "choose a seed"
                                onClicked: view.vm.browseSeed()
                            }
                        }
                    }

                    PixelIcon {
                        name: "MajoraMask"
                        Layout.preferredWidth: 52
                        Layout.preferredHeight: 52
                    }
                }
            }

            Text {
                Layout.fillWidth: true
                visible: view.vm.hasSeedError
                horizontalAlignment: Text.AlignRight
                wrapMode: Text.WordWrap
                text: view.vm.seedError
                color: theme.error
                font.family: theme.font
                font.pixelSize: 13
            }

            RowLayout {
                Layout.fillWidth: true
                spacing: 14

                GameTile {
                    Layout.fillWidth: true
                    Layout.preferredWidth: 1
                    Layout.alignment: Qt.AlignTop
                    card: view.vm.ootCard
                    onOpenMods: {
                        mods.card = view.vm.ootCard
                        mods.open()
                    }
                    onOpenModels: {
                        models.isOot = true
                        models.open()
                    }
                }

                GameTile {
                    Layout.fillWidth: true
                    Layout.preferredWidth: 1
                    Layout.alignment: Qt.AlignTop
                    card: view.vm.mmCard
                    onOpenMods: {
                        mods.card = view.vm.mmCard
                        mods.open()
                    }
                    onOpenModels: {
                        models.isOot = false
                        models.open()
                    }
                }
            }

            Flow {
                Layout.fillWidth: true
                visible: view.vm.showMultiworld
                spacing: 8

                PixelIcon {
                    name: "MoonsTear"
                    width: 28
                    height: 28
                }

                Chip {
                    text: view.vm.hostRelay ? "Hosting :" + view.vm.relayPort
                                            : "Joining " + (view.vm.multiworldAddress.length > 0 ? view.vm.multiworldAddress : "a relay")
                    lit: true
                    onClicked: relay.open()
                }

                Chip {
                    text: view.vm.playerName.length > 0 ? view.vm.playerName : "Set player name"
                    onClicked: relay.open()
                }

                Chip {
                    text: "PvP"
                    lit: view.vm.hostPvp
                    enabled: view.vm.hostRelay
                    onClicked: view.vm.hostPvp = !view.vm.hostPvp
                }

                Chip {
                    text: "Friendly fire"
                    lit: view.vm.hostFriendlyFire
                    enabled: !!(view.vm.hostRelay && view.vm.hostPvp)
                    onClicked: view.vm.hostFriendlyFire = !view.vm.hostFriendlyFire
                }

                Chip {
                    text: "Global death link"
                    lit: view.vm.hostDeathLink
                    enabled: view.vm.hostRelay
                    onClicked: view.vm.hostDeathLink = !view.vm.hostDeathLink
                }

                Text {
                    height: 28
                    verticalAlignment: Text.AlignVCenter
                    text: view.vm.hostRelay ? "Teammates always share deaths" : "Rules are set by the host"
                    color: theme.dim
                    font.family: theme.font
                    font.pixelSize: 12
                }
            }

            Text {
                Layout.fillWidth: true
                visible: text.length > 0
                horizontalAlignment: Text.AlignHCenter
                wrapMode: Text.WordWrap
                text: view.vm.statusMessage
                color: theme.warning
                font.family: theme.font
                font.pixelSize: 13
            }

            Repeater {
                model: view.vm.validationErrors

                delegate: Text {
                    required property var modelData
                    Layout.fillWidth: true
                    horizontalAlignment: Text.AlignHCenter
                    wrapMode: Text.WordWrap
                    text: modelData
                    color: theme.error
                    font.family: theme.font
                    font.pixelSize: 13
                }
            }

            Rectangle {
                Layout.alignment: Qt.AlignHCenter
                Layout.topMargin: 4
                implicitWidth: launch.implicitWidth + 10
                implicitHeight: launch.implicitHeight + 10
                radius: 13
                color: "transparent"
                border.width: 1
                border.color: theme.alpha(theme.accent, 0.4)

                Button {
                    id: launch
                    anchors.centerIn: parent
                    implicitWidth: 340
                    implicitHeight: 48
                    enabled: !view.vm.isSetupInProgress
                    hoverEnabled: true
                    onClicked: view.vm.launch()

                    contentItem: Item {
                        Row {
                            anchors.centerIn: parent
                            spacing: 12

                            PixelIcon {
                                name: "MasterSword"
                                width: 26
                                height: 26
                                anchors.verticalCenter: parent.verticalCenter
                            }

                            Text {
                                anchors.verticalCenter: parent.verticalCenter
                                text: view.vm.launchButtonText
                                color: launch.enabled ? theme.accentInk : theme.dim
                                font.family: theme.font
                                font.pixelSize: 18
                                font.weight: Font.DemiBold
                            }
                        }
                    }

                    background: Rectangle {
                        radius: 9
                        color: !launch.enabled ? theme.raised
                             : launch.down ? theme.accent
                             : launch.hovered ? theme.accentBright : theme.accent
                        border.width: 1.5
                        border.color: launch.enabled ? theme.accentBright : theme.alpha(theme.accent, 0.3)
                    }
                }
            }

            Text {
                Layout.fillWidth: true
                visible: view.vm.showSetupHint
                horizontalAlignment: Text.AlignHCenter
                wrapMode: Text.WordWrap
                text: "First-time setup: each game extracts its ROM assets, then the launcher builds the shared OoTMM gameplay assets."
                color: theme.dim
                font.family: theme.font
                font.pixelSize: 12
            }

            Text {
                Layout.fillWidth: true
                visible: view.vm.showMqHint
                horizontalAlignment: Text.AlignHCenter
                wrapMode: Text.WordWrap
                text: "This seed uses Master Quest dungeons, so an OoT Master Quest ROM is also needed."
                color: theme.dim
                font.family: theme.font
                font.pixelSize: 12
            }

            Row {
                Layout.alignment: Qt.AlignHCenter
                spacing: 8

                Text {
                    text: "Settings save to launcher.ui.json next to the launcher ·"
                    color: theme.faint
                    font.family: theme.font
                    font.pixelSize: 12
                }

                LinkButton {
                    text: "Preview the in-game sidebar"
                    font.pixelSize: 12
                    onClicked: view.vm.previewRuntime()
                }
            }
        }
    }

    ModsPopup {
        id: mods
    }

    ModelsPopup {
        id: models
        config: view.vm
    }

    RelayPopup {
        id: relay
        config: view.vm
    }
}
