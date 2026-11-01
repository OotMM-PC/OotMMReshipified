import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Dialogs
import Ui

ApplicationWindow {
    id: win

    visible: true
    width: 1280
    height: 820
    minimumWidth: 980
    minimumHeight: 640
    title: "OoTMM PC"
    color: theme.bgDeep
    font.family: theme.font
    font.pixelSize: 13

    Theme {
        id: theme
        violet: win.mainVm.accentIsViolet
    }

    Backend {
        id: app
    }

    readonly property var mainVm: app.main

    Timer {
        interval: 8
        repeat: true
        running: true
        onTriggered: app.pump()
    }

    onActiveChanged: if (active) app.windowActivated()

    Loader {
        anchors.fill: parent
        sourceComponent: win.mainVm.isRuntime ? runtimeView : configView
    }

    Component {
        id: configView

        ConfigView {
            vm: win.mainVm.config
        }
    }

    Component {
        id: runtimeView

        RuntimeView {
            vm: win.mainVm.runtime
        }
    }

    property int pickRequest: app.pickRequest
    onPickRequestChanged: picker.open()

    FileDialog {
        id: picker
        title: app.pickTitle
        nameFilters: [app.pickFilter, "All files (*)"]
        onAccepted: app.completePick(theme.localPath(selectedFile))
        onRejected: app.completePick("")
    }
}
