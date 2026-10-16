namespace OpenKogama.P2p;

enum Op : byte
{
    Register = 1,
    Registered,
    Find,
    Found,
    Missing,
    Relay,
    Relayed,
    Wrap,
    Unrelay,
    Open = 16,
    Data,
    Close,
    Datagram,
    Credit,
}
