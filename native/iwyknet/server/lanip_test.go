package server

import (
	"errors"
	"net"
	"testing"
)

func ips(s ...string) []net.IP {
	var out []net.IP
	for _, x := range s {
		out = append(out, net.ParseIP(x))
	}
	return out
}

func TestSelectLANIP(t *testing.T) {
	lo := Iface{Name: "lo0", Up: true, Loopback: true, Addrs: ips("127.0.0.1")}
	en0 := Iface{Name: "en0", Up: true, Addrs: ips("192.168.1.23")}
	utun := Iface{Name: "utun3", Up: true, Addrs: ips("10.8.0.2")}
	bridge := Iface{Name: "bridge100", Up: true, Addrs: ips("192.168.64.1")}
	down := Iface{Name: "en1", Up: false, Addrs: ips("192.168.5.5")}
	vbox := Iface{Name: "Ethernet 3", Desc: "VirtualBox Host-Only Ethernet Adapter", Up: true, Addrs: ips("192.168.56.1")}
	wifi := Iface{Name: "Wi-Fi", Up: true, Addrs: ips("10.0.0.40")}
	hyperv := Iface{Name: "vEthernet (WSL)", Up: true, Addrs: ips("172.20.0.1")}
	public := Iface{Name: "eth0", Up: true, Addrs: ips("203.0.113.5")}

	cases := []struct {
		name   string
		route  string
		ifaces []Iface
		want   string
		err    error
	}{
		{"route on en0", "192.168.1.23", []Iface{lo, utun, en0}, "192.168.1.23", nil},
		{"route over vpn falls back to en0", "10.8.0.2", []Iface{lo, utun, en0}, "192.168.1.23", nil},
		{"no route, prefer en over bridge", "", []Iface{lo, bridge, en0}, "192.168.1.23", nil},
		{"no route, bridge only", "", []Iface{lo, bridge}, "192.168.64.1", nil},
		{"down ignored", "", []Iface{lo, down, bridge}, "192.168.64.1", nil},
		{"windows virtual deprioritised", "192.168.56.1", []Iface{vbox, hyperv, wifi}, "10.0.0.40", nil},
		{"windows virtual only", "", []Iface{vbox}, "192.168.56.1", nil},
		{"public route used last", "203.0.113.5", []Iface{lo, public}, "203.0.113.5", nil},
		{"loopback only", "127.0.0.1", []Iface{lo}, "", ErrNoIP},
		{"nothing", "", nil, "", ErrNoIP},
		{"172.16/12 private", "", []Iface{{Name: "eth1", Up: true, Addrs: ips("172.31.9.9", "172.32.0.1")}}, "172.31.9.9", nil},
		{"ipv6 skipped", "", []Iface{{Name: "en0", Up: true, Addrs: ips("fe80::1", "10.1.2.3")}}, "10.1.2.3", nil},
	}
	for _, c := range cases {
		var route net.IP
		if c.route != "" {
			route = net.ParseIP(c.route)
		}
		got, err := SelectLANIP(route, c.ifaces)
		if c.err != nil {
			if !errors.Is(err, c.err) {
				t.Errorf("%s: err %v", c.name, err)
			}
			continue
		}
		if err != nil || got.String() != c.want {
			t.Errorf("%s: got %v %v, want %s", c.name, got, err, c.want)
		}
	}
}

func TestSystemLANIPDoesNotPanic(t *testing.T) {
	_, _ = SelectLANIP(RouteIP(), SystemInterfaces())
	if !IsLocalIP(net.ParseIP("127.0.0.1"), SystemInterfaces()) {
		t.Log("127.0.0.1 not listed on this host")
	}
}
