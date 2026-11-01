import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Layouts

PanelPopup {
    id: popup

    property var config

    title: "Multiworld connection"

    GoldCheck {
        Layout.fillWidth: true
        text: "Host the relay server on this machine"
        on: popup.config ? popup.config.hostRelay : false
        onFlipped: value => popup.config.hostRelay = value
    }

    GridLayout {
        Layout.fillWidth: true
        columns: 2
        columnSpacing: 12
        rowSpacing: 8

        Text {
            visible: !!(popup.config && popup.config.hostRelay)
            text: "Port"
            color: theme.text
            font.family: theme.font
            font.pixelSize: 13
        }

        Field {
            visible: !!(popup.config && popup.config.hostRelay)
            Layout.preferredWidth: 110
            text: popup.config ? popup.config.relayPort : ""
            validator: IntValidator { bottom: 1; top: 65535 }
            onEditingFinished: popup.config.relayPort = parseInt(text)
        }

        Text {
            visible: !!(popup.config && !popup.config.hostRelay)
            text: "Server address"
            color: theme.text
            font.family: theme.font
            font.pixelSize: 13
        }

        Field {
            visible: !!(popup.config && !popup.config.hostRelay)
            Layout.fillWidth: true
            placeholderText: "host:port"
            text: popup.config ? popup.config.multiworldAddress : ""
            onEditingFinished: popup.config.multiworldAddress = text
        }

        Text {
            text: "Player name"
            color: theme.text
            font.family: theme.font
            font.pixelSize: 13
        }

        Field {
            Layout.fillWidth: true
            text: popup.config ? popup.config.playerName : ""
            onEditingFinished: popup.config.playerName = text
        }
    }

    Text {
        Layout.fillWidth: true
        wrapMode: Text.WordWrap
        text: popup.config && popup.config.hostRelay
              ? "Other players connect to this machine's IP on that port."
              : "Ask the host for their address and port."
        color: theme.dim
        font.family: theme.font
        font.pixelSize: 12
    }
}
