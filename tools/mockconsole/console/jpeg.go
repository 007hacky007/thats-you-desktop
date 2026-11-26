package console

import (
	"bytes"
	"errors"
	"fmt"
	"image"
	"image/color"
	"image/jpeg"
	"strconv"
	"strings"
)

// ImageSpec describes a synthesized test image.
type ImageSpec struct {
	W     int    `json:"w"`
	H     int    `json:"h"`
	Color string `json:"color"`
	Label string `json:"label"`
}

// ParseColor reads "#rrggbb".
func ParseColor(s string) (color.RGBA, error) {
	if len(s) != 7 || s[0] != '#' {
		return color.RGBA{}, fmt.Errorf("colour %q: want #rrggbb", s)
	}
	v, err := strconv.ParseUint(s[1:], 16, 32)
	if err != nil {
		return color.RGBA{}, fmt.Errorf("colour %q: %v", s, err)
	}
	return color.RGBA{uint8(v >> 16), uint8(v >> 8), uint8(v), 255}, nil
}

// glyphs: a tiny 3x5 bitmap font of our own (rows top to bottom, 3 bits each).
var glyphs = map[rune][5]uint8{
	'A': {2, 5, 7, 5, 5}, 'B': {6, 5, 6, 5, 6}, 'C': {3, 4, 4, 4, 3}, 'D': {6, 5, 5, 5, 6},
	'E': {7, 4, 6, 4, 7}, 'F': {7, 4, 6, 4, 4}, 'G': {3, 4, 5, 5, 3}, 'H': {5, 5, 7, 5, 5},
	'I': {7, 2, 2, 2, 7}, 'J': {1, 1, 1, 5, 2}, 'K': {5, 5, 6, 5, 5}, 'L': {4, 4, 4, 4, 7},
	'M': {5, 7, 7, 5, 5}, 'N': {6, 5, 5, 5, 5}, 'O': {2, 5, 5, 5, 2}, 'P': {6, 5, 6, 4, 4},
	'Q': {2, 5, 5, 6, 3}, 'R': {6, 5, 6, 5, 5}, 'S': {3, 4, 2, 1, 6}, 'T': {7, 2, 2, 2, 2},
	'U': {5, 5, 5, 5, 7}, 'V': {5, 5, 5, 5, 2}, 'W': {5, 5, 7, 7, 5}, 'X': {5, 5, 2, 5, 5},
	'Y': {5, 5, 2, 2, 2}, 'Z': {7, 1, 2, 4, 7},
	'0': {7, 5, 5, 5, 7}, '1': {2, 6, 2, 2, 7}, '2': {6, 1, 2, 4, 7}, '3': {6, 1, 2, 1, 6},
	'4': {5, 5, 7, 1, 1}, '5': {7, 4, 6, 1, 6}, '6': {3, 4, 7, 5, 7}, '7': {7, 1, 2, 2, 2},
	'8': {7, 5, 7, 5, 7}, '9': {7, 5, 7, 1, 6}, '-': {0, 0, 7, 0, 0}, ' ': {0, 0, 0, 0, 0},
}

// SynthImage draws a solid colour image with a contrasting frame and label.
func SynthImage(w, h int, c color.RGBA, label string) *image.RGBA {
	img := image.NewRGBA(image.Rect(0, 0, w, h))
	lum := int(c.R)*299 + int(c.G)*587 + int(c.B)*114
	fg := color.RGBA{255, 255, 255, 255}
	if lum > 128000 {
		fg = color.RGBA{0, 0, 0, 255}
	}
	border := max(2, min(w, h)/40)
	for y := 0; y < h; y++ {
		for x := 0; x < w; x++ {
			if x < border || y < border || x >= w-border || y >= h-border {
				img.SetRGBA(x, y, fg)
			} else {
				img.SetRGBA(x, y, c)
			}
		}
	}
	label = strings.ToUpper(label)
	n := len([]rune(label))
	if n == 0 {
		return img
	}
	scale := max(1, min(w*3/4/(n*4), h/3/5))
	tw, th := n*4*scale-scale, 5*scale
	ox, oy := (w-tw)/2, (h-th)/2
	i := 0
	for _, r := range label {
		g, ok := glyphs[r]
		if !ok {
			g = glyphs['-']
		}
		for row := 0; row < 5; row++ {
			for col := 0; col < 3; col++ {
				if g[row]&(4>>col) == 0 {
					continue
				}
				for dy := 0; dy < scale; dy++ {
					for dx := 0; dx < scale; dx++ {
						img.SetRGBA(ox+(i*4+col)*scale+dx, oy+row*scale+dy, fg)
					}
				}
			}
		}
		i++
	}
	return img
}

// EncodeJPEG encodes with the phone JPEG rule of the contract (0.75, then
// 0.20 at >= 63489 bytes, then 0.10 over 128000 bytes).
func EncodeJPEG(img image.Image) ([]byte, error) {
	for _, q := range []int{75, 20, 10} {
		var b bytes.Buffer
		if err := jpeg.Encode(&b, img, &jpeg.Options{Quality: q}); err != nil {
			return nil, err
		}
		if (q == 75 && b.Len() < 63489) || (q != 75 && b.Len() <= 128000) {
			return b.Bytes(), nil
		}
	}
	return nil, errors.New("image too large for the JPEG rule")
}

// SynthJPEG builds a labelled solid colour JPEG.
func SynthJPEG(spec ImageSpec) ([]byte, error) {
	if spec.W <= 0 || spec.H <= 0 || spec.W > 4096 || spec.H > 4096 {
		return nil, fmt.Errorf("bad image size %dx%d", spec.W, spec.H)
	}
	col := color.RGBA{128, 128, 128, 255}
	if spec.Color != "" {
		c, err := ParseColor(spec.Color)
		if err != nil {
			return nil, err
		}
		col = c
	}
	return EncodeJPEG(SynthImage(spec.W, spec.H, col, spec.Label))
}

// JPEGInfo is what CheckJPEG found.
type JPEGInfo struct {
	Width, Height int
	Progressive   bool
}

// CheckJPEG validates the container structure: SOI at the start, EOI at the
// end, well-formed segments up to SOS and a SOF with the dimensions; it then
// decodes the image to make sure the entropy-coded data is sound.
func CheckJPEG(data []byte) (JPEGInfo, error) {
	var info JPEGInfo
	if len(data) < 4 || data[0] != 0xFF || data[1] != 0xD8 {
		return info, errors.New("jpeg: missing SOI")
	}
	if data[len(data)-2] != 0xFF || data[len(data)-1] != 0xD9 {
		return info, errors.New("jpeg: missing EOI")
	}
	i := 2
	sof := false
	for {
		for i < len(data) && data[i] == 0xFF && i+1 < len(data) && data[i+1] == 0xFF {
			i++ // fill bytes
		}
		if i+4 > len(data) || data[i] != 0xFF {
			return info, fmt.Errorf("jpeg: bad marker at %d", i)
		}
		m := data[i+1]
		if m == 0xD9 {
			return info, errors.New("jpeg: EOI before SOS")
		}
		if m == 0x01 || (m >= 0xD0 && m <= 0xD7) {
			i += 2
			continue
		}
		seg := int(data[i+2])<<8 | int(data[i+3])
		if seg < 2 || i+2+seg > len(data) {
			return info, fmt.Errorf("jpeg: segment %02X overruns the file", m)
		}
		switch m {
		case 0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7, 0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF:
			if seg < 8 {
				return info, errors.New("jpeg: short SOF")
			}
			info.Height = int(data[i+5])<<8 | int(data[i+6])
			info.Width = int(data[i+7])<<8 | int(data[i+8])
			info.Progressive = m == 0xC2 || m == 0xC6 || m == 0xCA || m == 0xCE
			sof = true
		}
		if m == 0xDA {
			if !sof {
				return info, errors.New("jpeg: SOS before SOF")
			}
			break
		}
		i += 2 + seg
	}
	if info.Width == 0 || info.Height == 0 {
		return info, errors.New("jpeg: zero dimension")
	}
	img, err := jpeg.Decode(bytes.NewReader(data))
	if err != nil {
		return info, fmt.Errorf("jpeg: decode: %v", err)
	}
	if b := img.Bounds(); b.Dx() != info.Width || b.Dy() != info.Height {
		return info, errors.New("jpeg: decoded size differs from SOF")
	}
	return info, nil
}
