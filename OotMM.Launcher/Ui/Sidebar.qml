import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts

Item {
    id: side

    property var vm
    readonly property real s: vm ? vm.sidebarScale : 1
    readonly property var mapVm: vm ? vm.map : null
    readonly property bool wide: !!(mapVm && mapVm.expanded && vm.selectedTabIndex === vm.mapTabIndex)
    readonly property int fullWidth: wide ? 760 : 380
    readonly property var tabs: [
        { icon: "DungeonMap", label: "Tracker" },
        { icon: "BombersNotebook", label: "Log" },
        { icon: "KokiriSword", label: "Session" },
        { icon: "LensOfTruth", label: "Options" },
        { icon: "Compass", label: "Progress" },
        { icon: "Map", label: "Map" }
    ]

    implicitWidth: !vm ? 0 : vm.isFinalizingSync ? 380 : vm.showFullSidebar ? Math.round(fullWidth * s) : 44

    Rectangle {
        anchors.fill: parent
        visible: !!(side.vm && side.vm.isFinalizingSync)
        color: theme.panel

        ColumnLayout {
            anchors.fill: parent
            anchors.margins: 16
            spacing: 10

            Text {
                text: "Finalizing multiworld sync"
                color: theme.accentBright
                font.family: theme.font
                font.pixelSize: 15
                font.weight: Font.DemiBold
            }

            Text {
                Layout.fillWidth: true
                wrapMode: Text.WordWrap
                text: "Waiting for every received item to be written safely. This window continues on its own."
                color: theme.dim
                font.family: theme.font
                font.pixelSize: 13
            }

            Item {
                Layout.fillHeight: true
            }
        }
    }

    Button {
        anchors.fill: parent
        anchors.margins: 10
        anchors.leftMargin: 0
        visible: !!(side.vm && side.vm.showCollapsedStrip)
        hoverEnabled: true
        onClicked: side.vm.expandSidebar()
        ToolTip.visible: hovered
        ToolTip.text: "Expand the sidebar"

        contentItem: Text {
            text: "‹"
            color: theme.accent
            font.pixelSize: 20
            horizontalAlignment: Text.AlignHCenter
            verticalAlignment: Text.AlignVCenter
        }

        background: Rectangle {
            radius: 8
            color: parent.hovered ? theme.raised : theme.panel
            border.width: 1
            border.color: theme.alpha(theme.accent, 0.35)
        }
    }

    Item {
        id: full
        visible: !!(side.vm && side.vm.showFullSidebar)
        width: side.fullWidth
        height: side.height / side.s
        scale: side.s
        transformOrigin: Item.TopLeft

        Rectangle {
            anchors.fill: parent
            color: theme.panel
        }

        Rectangle {
            width: 2
            height: parent.height
            color: theme.alpha(theme.accent, 0.5)
        }

        ColumnLayout {
            anchors.fill: parent
            anchors.margins: 14
            anchors.leftMargin: 16
            spacing: 10

            RowLayout {
                Layout.fillWidth: true
                spacing: 8

                Triforce {
                    Layout.preferredWidth: 22
                    Layout.preferredHeight: 20
                }

                Text {
                    Layout.fillWidth: true
                    text: "OoTMM"
                    color: theme.accentBright
                    font.family: theme.font
                    font.pixelSize: 18
                    font.weight: Font.DemiBold
                }

                Button {
                    implicitWidth: 30
                    implicitHeight: 30
                    hoverEnabled: true
                    onClicked: side.vm.collapseSidebar()
                    ToolTip.visible: hovered
                    ToolTip.text: "Collapse the sidebar"

                    contentItem: Text {
                        text: "›"
                        color: theme.accent
                        font.pixelSize: 18
                        horizontalAlignment: Text.AlignHCenter
                        verticalAlignment: Text.AlignVCenter
                    }

                    background: Rectangle {
                        radius: 6
                        color: parent.hovered ? theme.raised : "transparent"
                        border.width: 1
                        border.color: theme.alpha(theme.accent, 0.45)
                    }
                }
            }

            Rectangle {
                Layout.fillWidth: true
                implicitHeight: banner.implicitHeight + 20
                radius: 8
                color: theme.raised
                border.width: 1
                border.color: theme.alpha(theme.accent, 0.45)

                RowLayout {
                    id: banner
                    anchors.fill: parent
                    anchors.margins: 10
                    spacing: 10

                    Rectangle {
                        Layout.preferredWidth: 42
                        Layout.preferredHeight: 42
                        radius: 7
                        color: theme.bgDeep
                        border.width: 1
                        border.color: theme.alpha(theme.accent, 0.35)

                        PixelIcon {
                            anchors.centerIn: parent
                            name: side.vm && side.vm.activeIsOot ? "OcarinaOfTime" : "FierceDeityMask"
                            width: 30
                            height: 30
                        }
                    }

                    ColumnLayout {
                        Layout.fillWidth: true
                        spacing: 1

                        Text {
                            Layout.fillWidth: true
                            text: side.vm ? side.vm.activeGameTitle : ""
                            color: theme.accentBright
                            font.family: theme.font
                            font.pixelSize: 15
                            font.weight: Font.DemiBold
                        }

                        Text {
                            Layout.fillWidth: true
                            text: "● " + (side.vm ? side.vm.runStatusText : "")
                            color: side.vm && side.vm.isRunning ? theme.success : theme.error
                            font.family: theme.font
                            font.pixelSize: 12
                        }

                        Text {
                            Layout.fillWidth: true
                            visible: !!(side.vm && side.vm.showMultiworldStatus)
                            text: side.vm ? side.vm.multiworldStatus : ""
                            elide: Text.ElideRight
                            color: theme.dim
                            font.family: theme.font
                            font.pixelSize: 11
                        }
                    }
                }
            }

            RowLayout {
                Layout.fillWidth: true
                spacing: 4

                Repeater {
                    model: side.tabs

                    delegate: Button {
                        id: tab
                        required property var modelData
                        required property int index
                        readonly property bool active: side.vm && side.vm.selectedTabIndex === index
                        visible: !side.vm || index !== side.vm.mapTabIndex || side.vm.showMapTab
                        Layout.fillWidth: true
                        implicitHeight: 54
                        hoverEnabled: true
                        onClicked: side.vm.selectedTabIndex = index

                        contentItem: Column {
                            spacing: 2

                            PixelIcon {
                                anchors.horizontalCenter: parent.horizontalCenter
                                name: tab.modelData.icon
                                width: 26
                                height: 26
                            }

                            Text {
                                anchors.horizontalCenter: parent.horizontalCenter
                                text: tab.modelData.label
                                color: tab.active ? theme.accentBright : theme.dim
                                font.family: theme.font
                                font.pixelSize: 10
                            }
                        }

                        background: Rectangle {
                            radius: 7
                            color: tab.active ? theme.raised : tab.hovered ? theme.alpha(theme.accent, 0.08) : "transparent"
                            border.width: 1
                            border.color: tab.active ? theme.accent : "transparent"
                        }
                    }
                }
            }

            Loader {
                Layout.fillWidth: true
                Layout.fillHeight: true
                sourceComponent: !side.vm ? null
                               : side.vm.selectedTabIndex === 0 ? trackerTab
                               : side.vm.selectedTabIndex === 1 ? logTab
                               : side.vm.selectedTabIndex === 2 ? sessionTab
                               : side.vm.selectedTabIndex === 3 ? optionsTab
                               : side.vm.selectedTabIndex === 4 ? progressTab : mapTab
            }
        }
    }

    Component {
        id: trackerTab
        TrackerTab { vm: side.vm.tracker }
    }

    Component {
        id: logTab
        LogTab { vm: side.vm.log }
    }

    Component {
        id: sessionTab
        SessionTab { vm: side.vm.sessionTab }
    }

    Component {
        id: optionsTab
        OptionsTab { vm: side.vm.options }
    }

    Component {
        id: progressTab
        ProgressTab { vm: side.vm.progress }
    }

    Component {
        id: mapTab
        MapTab {
            vm: side.mapVm
            wide: side.wide
        }
    }
}
