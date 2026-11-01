import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts

PanelPopup {
    id: popup

    property var config
    property bool isOot: true
    property var card: config ? (isOot ? config.ootCard : config.mmCard) : null
    property var picks: config ? (isOot ? config.ootModelPicks : config.mmModelPicks) : []

    title: isOot ? "Ocarina of Time player model" : "Majora's Mask player model"
    tint: isOot ? theme.gold : theme.mm

    Text {
        Layout.fillWidth: true
        wrapMode: Text.WordWrap
        text: "Check every archive your model needs: child, adult, textures and voice. In co-op the set is shared with the other players. With none checked, Link stays vanilla."
        color: theme.dim
        font.family: theme.font
        font.pixelSize: 12
    }

    ListView {
        id: list
        Layout.fillWidth: true
        Layout.preferredHeight: Math.min(contentHeight, 260)
        clip: true
        model: popup.picks
        ScrollBar.vertical: ScrollBar { }

        delegate: GoldCheck {
            required property var modelData
            width: list.width - 12
            tint: popup.tint
            text: modelData.name
            checked: modelData.isSelected
            onToggled: modelData.isSelected = checked
        }
    }

    Text {
        visible: list.count === 0
        text: "No model archives in this game's mods folder."
        color: theme.faint
        font.family: theme.font
        font.pixelSize: 12
    }

    ColumnLayout {
        Layout.fillWidth: true
        visible: !popup.isOot
        spacing: 6

        Rectangle {
            Layout.fillWidth: true
            height: 1
            color: theme.alpha(popup.tint, 0.25)
        }

        GoldCheck {
            Layout.fillWidth: true
            tint: popup.tint
            text: "Use my OoT model for adult Link"
            checked: popup.card ? popup.card.portAdultModel : false
            onToggled: popup.card.portAdultModel = checked
        }

        GoldCheck {
            Layout.fillWidth: true
            tint: popup.tint
            text: "Use my OoT child model as MM's human Link"
            checked: popup.card ? popup.card.portChildModel : false
            onToggled: popup.card.portChildModel = checked
        }

        Text {
            Layout.fillWidth: true
            wrapMode: Text.WordWrap
            text: "The child model overrides MM human-form model mods. Deku, Goron, Zora and Fierce Deity keep their MM mods."
            color: theme.dim
            font.family: theme.font
            font.pixelSize: 12
        }
    }
}
