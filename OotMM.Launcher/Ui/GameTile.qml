import QtQuick
import QtQuick.Layouts

BracketFrame {
    id: tile

    property var card
    property color tint: card && card.isOot ? theme.gold : theme.mm
    property color tintBright: card && card.isOot ? theme.goldBright : theme.mmBright
    property string iconName: card && card.isOot ? "OcarinaOfTime" : "FierceDeityMask"

    signal openMods()
    signal openModels()

    bracketColor: tint
    padding: 18
    implicitHeight: column.implicitHeight + 36

    function statusLine() {
        if (!card)
            return ""
        if (!card.isReady)
            return card.statusText
        var parts = ["● Ready", card.enabledModCount + (card.enabledModCount === 1 ? " mod" : " mods")]
        if (!card.isOot && card.portAdultModel)
            parts.push("adult uses OoT model")
        return parts.join(" · ")
    }

    ColumnLayout {
        id: column
        anchors.left: parent.left
        anchors.right: parent.right
        spacing: 8

        PixelIcon {
            name: tile.iconName
            Layout.alignment: Qt.AlignHCenter
            Layout.preferredWidth: 64
            Layout.preferredHeight: 64
        }

        Text {
            Layout.alignment: Qt.AlignHCenter
            text: tile.card ? tile.card.title : ""
            color: tile.tintBright
            font.family: theme.font
            font.pixelSize: 20
            font.weight: Font.DemiBold
        }

        Text {
            Layout.alignment: Qt.AlignHCenter
            Layout.topMargin: -6
            text: tile.card ? tile.card.portSubtitle : ""
            color: theme.dim
            font.family: theme.font
            font.pixelSize: 13
        }

        Text {
            Layout.fillWidth: true
            horizontalAlignment: Text.AlignHCenter
            wrapMode: Text.WordWrap
            text: tile.statusLine()
            color: !tile.card ? theme.dim
                 : tile.card.isReady ? theme.success
                 : tile.card.statusIsError ? theme.error : theme.warning
            font.family: theme.font
            font.pixelSize: 13
        }

        Field {
            id: path
            Layout.fillWidth: true
            tint: tile.tint
            text: tile.card ? tile.card.exePath : ""
            placeholderText: tile.card ? tile.card.exeHint : ""
            onEditingFinished: if (tile.card && text !== tile.card.exePath) tile.card.exePath = text
        }

        Row {
            Layout.alignment: Qt.AlignHCenter
            spacing: 24

            LinkButton {
                text: "Browse"
                tint: tile.tint
                onClicked: tile.card.browseExe()
            }

            LinkButton {
                text: "Mods"
                tint: tile.tint
                enabled: !!(tile.card && tile.card.hasMods)
                onClicked: tile.openMods()
            }

            LinkButton {
                text: "Models"
                tint: tile.tint
                onClicked: tile.openModels()
            }
        }

        Text {
            Layout.fillWidth: true
            visible: !!(tile.card && tile.card.hasModelPortStatus)
            horizontalAlignment: Text.AlignHCenter
            wrapMode: Text.WordWrap
            text: tile.card ? tile.card.modelPortStatus : ""
            color: theme.dim
            font.family: theme.font
            font.pixelSize: 12
        }
    }
}
