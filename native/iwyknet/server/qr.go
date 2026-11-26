package server

import (
	"fmt"

	qrcode "github.com/skip2/go-qrcode"
)

// QR encodes text as a QR code with error correction level M and no quiet
// zone. modules holds side*side bytes row-major, 1 = dark, 0 = light.
func QR(text string) (side int, modules []byte, err error) {
	if text == "" {
		return 0, nil, fmt.Errorf("%w: empty QR text", ErrArg)
	}
	q, err := qrcode.New(text, qrcode.Medium)
	if err != nil {
		return 0, nil, fmt.Errorf("%w: %v", ErrArg, err)
	}
	q.DisableBorder = true
	bm := q.Bitmap()
	side = len(bm)
	modules = make([]byte, side*side)
	for y, row := range bm {
		if len(row) != side {
			return 0, nil, fmt.Errorf("%w: QR bitmap not square", ErrInternal)
		}
		for x, dark := range row {
			if dark {
				modules[y*side+x] = 1
			}
		}
	}
	return side, modules, nil
}
