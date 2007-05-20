/*
This program is part of BruNet, a library for the creation of efficient overlay
networks.
Copyright (C) 2007 P. Oscar Boykin <boykin@pobox.com> University of Florida

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
*/

namespace Brunet {

public class AHSender : ISender {

  protected Node _n;
  protected readonly Address _dest;
  public Address Destination { get { return _dest; } }
  protected readonly Address _source;
  public Address Source { get { return _source; } }
  protected short _hops;
  public short Hops { get { return _hops; } }
  protected short _ttl;
  public short Ttl { get { return _ttl; } }
  protected ushort _options;
  public ushort Options { get { return _options; } }

  protected ISender _from;
  /*
   * Every packet comes from somewhere, it is either locally generated,
   * or it came from an edge.  This ISender sends "back" from where the
   * packet came from
   *
   * If this a local packet, it was Received from the LocalSender
   */
  public ISender ReceivedFrom { get { return _from; } }
  //This is the serialized header:
  protected volatile MemBlock _header;

  public AHSender(Node n, Address destination, ushort options)
  : this( n, new LocalSender(n), destination, n.DefaultTTLFor(destination), options) {

  }

  public AHSender(Node n, Address destination, short ttl, ushort options)
    : this(n, new LocalSender(n), destination, ttl, options) {

  }
  public AHSender(Node n, ISender from, Address destination, short ttl, ushort options) {
    _n = n;
    _from = from;
    //Here are the fields in the order they appear:
    _hops = 0;
    _ttl = ttl;
    _source = n.Address;
    _dest = destination;
    _options = options;
  }

  /**
   * This is probably the most commonly used AHSender
   */
  public AHSender(Node n, Address destination)
    : this(n, destination, n.DefaultTTLFor(destination),
           AHPacket.AHOptions.AddClassDefault) {
    
  }

  override public bool Equals(object o) {
    AHSender ahs = o as AHSender;
    bool eq = false;
    if( ahs != null ) {
      eq = ahs.Destination.Equals( _dest );
      eq &= ( ahs._options == _options );
    }
    return eq;
  }

  override public int GetHashCode() {
    return _dest.GetHashCode();
  }

  //Be Lazy about doing this:
  protected MemBlock MakeHeader() {
    //Make the header part:
    byte[] header = new byte[ 47 ];
    int offset = 0;
    offset += PType.Protocol.AH.CopyTo(header, offset);
    //Write hops:
    NumberSerializer.WriteShort(_hops, header, offset);
    offset += 2;
    NumberSerializer.WriteShort(_ttl, header, offset);
    offset += 2;
    offset += _source.CopyTo(header, offset);
    offset += _dest.CopyTo(header, offset);
    NumberSerializer.WriteShort((short)_options, header, offset);
    offset += 2;
    return MemBlock.Reference(header, 0, offset);
  }
 
  public void Send(ICopyable data) {
    /*
     * Assemble an AHPacket:
     */
    if( _header == null ) { _header = MakeHeader(); }
    int total = _header.Length + data.Length;
    byte[] ah_packet = new byte[ total ];
    int off_to_data = _header.CopyTo(ah_packet, 0);
    data.CopyTo(ah_packet, off_to_data);
    MemBlock mb_packet = MemBlock.Reference(ah_packet);
    /*
     * Now we announce this packet, the AHHandler will
     * handle routing it for us
     */
    _n.Announce(mb_packet, _from);
  }

}

/**
 * This handles AHPackets which arrive at the node
 */
public class AHHandler : IDataHandler {

  protected AHRouter _ah_router;
  protected DirectionalRouter _d_router;
  protected Node _n;

  /**
   * You still need to Subscribe this.  This constructor DOES NOT
   * do that
   */
  public AHHandler(Node n) {
    _n = n;
    _ah_router = new AHRouter((AHAddress)n.Address);
    _d_router = new DirectionalRouter((AHAddress)n.Address);
    _ah_router.ConnectionTable = _n.ConnectionTable;
    _d_router.ConnectionTable = _n.ConnectionTable;
  }
  /**
   * Here we handle routing AHPackets
   */
  public void HandleData(MemBlock data, ISender ret_path, object state) {
    /*
     * Unfortunately, the old code needs the full header intact, and
     * we have already eaten a byte of it, put it back:
     */
    MemBlock full_packet = data.ExtendHead(1);
    AHPacket p = new AHPacket(full_packet);
    bool deliver_locally;
    //Route avoiding the edge we got the packet from:
    IRouter router = null;
    if( p.Destination.Class == 0 ) {
      router = _ah_router;
    }
    else {
      router = _d_router;
    }
    router.Route(ret_path as Edge, p, out deliver_locally);
    if( deliver_locally ) {
      ISender resp_send = new AHSender(_n, p.Source);
      //There are 2 (hops) + 2 (ttl) + 20 (s) + 20 (d) + 2 (opts) = 46 bytes to the payload encapsulated
      //data:
      _n.Announce( data.Slice(46), resp_send ); 
    }

  }

}

}
