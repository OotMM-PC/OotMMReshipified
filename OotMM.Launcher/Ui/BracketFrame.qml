import QtQuick

Item {
    id: frame

    property color bracketColor: theme.gold
    property color fill: theme.panel
    property int arm: 16
    property int thickness: 2
    property int padding: 16
    default property alias content: inner.data

    Rectangle {
        anchors.fill: parent
        radius: 8
        color: frame.fill
    }

    Repeater {
        model: 4
        delegate: Item {
            required property int index
            width: frame.arm
            height: frame.arm
            x: index % 2 === 0 ? 0 : frame.width - width
            y: index < 2 ? 0 : frame.height - height

            Rectangle {
                width: parent.width
                height: frame.thickness
                y: index < 2 ? 0 : parent.height - height
                color: frame.bracketColor
            }

            Rectangle {
                width: frame.thickness
                height: parent.height
                x: index % 2 === 0 ? 0 : parent.width - width
                color: frame.bracketColor
            }
        }
    }

    Item {
        id: inner
        anchors.fill: parent
        anchors.margins: frame.padding
    }
}
