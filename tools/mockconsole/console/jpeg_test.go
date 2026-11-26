package console

import (
	"bytes"
	"image"
	"image/color"
	"image/png"
	"strings"
	"testing"
)

func TestSynthJPEGRoundTrip(t *testing.T) {
	for _, sz := range [][2]int{{480, 640}, {384, 512}, {128, 128}, {360, 480}} {
		b, err := SynthJPEG(ImageSpec{W: sz[0], H: sz[1], Color: "#cc6633", Label: "co1"})
		if err != nil {
			t.Fatal(err)
		}
		if len(b) > 128000 {
			t.Errorf("too big: %d", len(b))
		}
		info, err := CheckJPEG(b)
		if err != nil {
			t.Fatal(err)
		}
		if info.Width != sz[0] || info.Height != sz[1] {
			t.Errorf("size %dx%d", info.Width, info.Height)
		}
	}
}

func TestSynthLabelDrawn(t *testing.T) {
	plain := SynthImage(100, 100, mustColor(t, "#000000"), "")
	lab := SynthImage(100, 100, mustColor(t, "#000000"), "A")
	if plain.RGBAAt(50, 50) == lab.RGBAAt(50, 50) && bytes.Equal(plain.Pix, lab.Pix) {
		t.Error("label not drawn")
	}
}

func mustColor(t *testing.T, s string) color.RGBA {
	t.Helper()
	col, err := ParseColor(s)
	if err != nil {
		t.Fatal(err)
	}
	return col
}

func TestCheckJPEGRejects(t *testing.T) {
	good, _ := SynthJPEG(ImageSpec{W: 48, H: 64})
	var pngBuf bytes.Buffer
	png.Encode(&pngBuf, image.NewRGBA(image.Rect(0, 0, 4, 4)))
	cases := map[string][]byte{
		"SOI":     pngBuf.Bytes(),
		"EOI":     good[:len(good)-2],
		"marker":  append(append([]byte{0xFF, 0xD8, 0x00, 0x00}, good[2:10]...), 0xFF, 0xD9),
		"overrun": {0xFF, 0xD8, 0xFF, 0xE0, 0x40, 0x00, 0xFF, 0xD9},
		"decode":  append(append(append([]byte{}, good[:len(good)/2]...), bytes.Repeat([]byte{0}, 10)...), 0xFF, 0xD9),
		"empty":   {},
	}
	for name, b := range cases {
		if _, err := CheckJPEG(b); err == nil {
			t.Errorf("%s: accepted", name)
		}
	}
	if _, err := CheckJPEG(good); err != nil {
		t.Errorf("good rejected: %v", err)
	}
}

func TestParseColor(t *testing.T) {
	if _, err := ParseColor("red"); err == nil || !strings.Contains(err.Error(), "#rrggbb") {
		t.Error("bad colour accepted")
	}
	c, _ := ParseColor("#102030")
	if c.R != 0x10 || c.G != 0x20 || c.B != 0x30 {
		t.Error(c)
	}
}
