import QtQuick

Image {
    property string name: ""

    source: name.length > 0 ? theme.icon(name) : ""
    smooth: false
    mipmap: false
    fillMode: Image.PreserveAspectFit
    sourceSize.width: 64
    sourceSize.height: 64
}
