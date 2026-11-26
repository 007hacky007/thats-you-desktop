package server

import (
	"fmt"
	"net"
	"sort"
	"strings"
)

// Iface describes one network interface for LAN IP selection.
type Iface struct {
	Name     string
	Desc     string // adapter description where the OS has one (Windows)
	Up       bool
	Loopback bool
	Addrs    []net.IP
}

var tunnelPrefixes = []string{"utun", "tun", "tap", "ppp", "wg", "ipsec", "gif", "stf"}
var virtualWords = []string{"vpn", "tap", "wireguard", "hyper-v", "virtualbox", "vmware", "vethernet"}
var preferredPrefixes = []string{"en", "eth", "wlan", "wi-fi", "ethernet"}

func isPrivate4(ip net.IP) bool {
	v4 := ip.To4()
	if v4 == nil {
		return false
	}
	return v4[0] == 10 || (v4[0] == 172 && v4[1]&0xf0 == 16) || (v4[0] == 192 && v4[1] == 168)
}

func (f Iface) tunnel() bool {
	n := strings.ToLower(f.Name)
	for _, p := range tunnelPrefixes {
		if strings.HasPrefix(n, p) {
			return true
		}
	}
	return false
}

func (f Iface) virtual() bool {
	s := strings.ToLower(f.Name + " " + f.Desc)
	for _, w := range virtualWords {
		if strings.Contains(s, w) {
			return true
		}
	}
	return false
}

func (f Iface) preferred() bool {
	n := strings.ToLower(f.Name)
	for _, p := range preferredPrefixes {
		if strings.HasPrefix(n, p) {
			return true
		}
	}
	return false
}

func (f Iface) usable() bool { return f.Up && !f.Loopback && !f.tunnel() }

// SelectLANIP implements contract 3.4. routeIP is the local address the OS
// picks for a route to 192.0.2.1 (nil when unknown).
func SelectLANIP(routeIP net.IP, ifaces []Iface) (net.IP, error) {
	if routeIP != nil && isPrivate4(routeIP) {
		for _, f := range ifaces {
			if !f.usable() || f.virtual() {
				continue
			}
			for _, a := range f.Addrs {
				if a.Equal(routeIP) {
					return routeIP.To4(), nil
				}
			}
		}
	}
	type cand struct {
		ip        net.IP
		virtual   bool
		preferred bool
		order     int
	}
	var cs []cand
	for _, f := range ifaces {
		if !f.usable() {
			continue
		}
		for _, a := range f.Addrs {
			if isPrivate4(a) {
				cs = append(cs, cand{a.To4(), f.virtual(), f.preferred(), len(cs)})
			}
		}
	}
	sort.SliceStable(cs, func(i, j int) bool {
		if cs[i].virtual != cs[j].virtual {
			return !cs[i].virtual
		}
		if cs[i].preferred != cs[j].preferred {
			return cs[i].preferred
		}
		return cs[i].order < cs[j].order
	})
	if len(cs) > 0 {
		return cs[0].ip, nil
	}
	if routeIP != nil && routeIP.To4() != nil && !routeIP.IsLoopback() && !routeIP.IsUnspecified() {
		return routeIP.To4(), nil
	}
	return nil, fmt.Errorf("%w: pass lan_ip", ErrNoIP)
}

// SystemInterfaces lists the host's interfaces with their IPv4 addresses.
func SystemInterfaces() []Iface {
	list, err := net.Interfaces()
	if err != nil {
		return nil
	}
	var out []Iface
	for _, ni := range list {
		f := Iface{Name: ni.Name, Up: ni.Flags&net.FlagUp != 0, Loopback: ni.Flags&net.FlagLoopback != 0}
		addrs, _ := ni.Addrs()
		for _, a := range addrs {
			if n, ok := a.(*net.IPNet); ok && n.IP.To4() != nil {
				f.Addrs = append(f.Addrs, n.IP.To4())
			}
		}
		out = append(out, f)
	}
	return out
}

// RouteIP returns the local address used for a route to 192.0.2.1:9
// (a UDP dial sends nothing), or nil.
func RouteIP() net.IP {
	c, err := net.Dial("udp4", "192.0.2.1:9")
	if err != nil {
		return nil
	}
	defer c.Close()
	if a, ok := c.LocalAddr().(*net.UDPAddr); ok {
		return a.IP.To4()
	}
	return nil
}

// IsLocalIP reports whether ip is assigned to one of the given interfaces.
func IsLocalIP(ip net.IP, ifaces []Iface) bool {
	for _, f := range ifaces {
		for _, a := range f.Addrs {
			if a.Equal(ip) {
				return true
			}
		}
	}
	return false
}
