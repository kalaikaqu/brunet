/*
This program is part of BruNet, a library for the creation of efficient overlay
networks.
Copyright (C) 2005  University of California

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

#define POB_DEBUG
#define TRIM

using System;
using System.Collections;

namespace Brunet {

  /**
   * This is an attempt to write a simple version of
   * StructuredConnectionOverlord which is currently quite complex,
   * difficult to understand, and difficult to debug.
   */
  public class SimpleConnectionOverlord : ConnectionOverlord {
    
    public SimpleConnectionOverlord(Node n)
    {
      _node = n;
      _rand = new Random();
      _sync = new Object();
      _connectors = new ArrayList();
      _last_connection_time = DateTime.Now;
      lock( _sync ) {
      //Listen for connection events:
        _node.ConnectionTable.DisconnectionEvent +=
          new EventHandler(this.DisconnectHandler);
        _node.ConnectionTable.ConnectionEvent +=
          new EventHandler(this.ConnectHandler);      
      /**
       * Every heartbeat we assess the trimming situation.
       * If we have excess edges and it has been more than
       * _trim_wait_time heartbeats then we trim.
       */
        _node.HeartBeatEvent += new EventHandler(this.CheckState);
      }
    }

    ///////  Attributes /////////////////

    protected Node _node;
    protected Random _rand;

    protected bool _compensate;
    //We use this to make sure we don't trim connections
    //too fast.  We want to only trim in the "steady state"
    protected DateTime _last_connection_time;
    protected object _sync;

    protected ArrayList _connectors;

    /**
     * Getting shortcuts on small networks is
     * hard, for now, we only try once when we need one.
     *
     * Everytime we get one, we set this to false,
     * Everytime we try one, we set this to true,
     */
    protected bool _tried_shortcut = false;
    
    /*
     * These are parameters of the Overlord.  These govern
     * the way it reacts and works.
     */
    
    ///How many neighbors do we want (same value for left and right)
    static protected readonly int _desired_neighbors = 2;
    static protected readonly int _desired_shortcuts = 1;
    ///How many seconds to wait between connections/disconnections to trim
    static protected readonly double _trim_delay = 30.0;
    
    /*
     * We don't want to risk mistyping these strings.
     */
    static protected readonly string struc_near = "structured.near";
    static protected readonly string struc_short = "structured.shortcut";
    
    /**
     * If we start compensating, we check to see if we need to
     * make new neighbor or shortcut connections : 
     */
    override public bool IsActive
    {
      get { return _compensate; }
      set { _compensate = value; }
    }    

    override public bool NeedConnection 
    {
      get {
	int structs = _node.ConnectionTable.Count(ConnectionType.Structured);
	if( structs < (2 * _desired_neighbors + _desired_shortcuts) ) {
          //We don't have enough connections for what we need:
          return true;
	}
	else {
          //The total is enough, but we may be missing some edges
          return NeedShortcut || NeedLeftNeighbor || NeedRightNeighbor;
	}
      }
    }
    
    /*
     * In between connections or disconnections there is no
     * need to recompute whether we need connections.
     * So after each connection or disconnection, this becomes
     * false.
     *
     * These are -1 when we don't know 0 is false, 1 is true
     *
     * This is just an optimization, however, running many nodes
     * on one computer seems to benefit from this optimization
     * (reducing cpu usage and thus the likely of timeouts).
     */
    protected int _need_left;
    protected int _need_right;
    protected int _need_short;
    /**
     * We know a left neighbor from a right neighbor because it is closer
     * on the left hand side.  Strictly speaking, this is not necessarily true
     * but for networks greated than size 10 it is very likely:
     *
     * The probabililty that there are less than k neighbors on one half of the ring:
     * \frac{1}{2^N} \sum_{i=0}^{k-1} {N \choose k}
     * for k=2: (1+N)/(2^N)
     * For N=10, the probability that a node is in this boat is already ~1/100.  For
     * N ~ 100 this will never happen in the life of the universe.
     */

    /**
     * 
     * @returns true if we have too few left neighbor connections
     */
    protected bool NeedLeftNeighbor {
      get {
	lock( _sync ) {
          if( _need_left != -1 ) {
            return (_need_left == 1);
	  }
	  int left = 0;
	  AHAddress local = (AHAddress)_node.Address;
	  ConnectionTable tab = _node.ConnectionTable;
	  lock( tab.SyncRoot ) {
          //foreach(Connection c in _node.ConnectionTable.GetConnections(struc_near)) {
            foreach(Connection c in tab.GetConnections(ConnectionType.Structured)) {
              AHAddress adr = (AHAddress)c.Address;
//#if POB_DEBUG
#if false
              Console.WriteLine("{0} -> {1}, lidx: {2}, is_left: {3}" ,
			    _node.Address, adr, LeftPosition(adr), adr.IsLeftOf( local ) );
#endif
	      if( 
	        //adr.IsLeftOf( local ) &&
                (LeftPosition( adr ) < _desired_neighbors) ) {
                //This is left neighbor:
	        left++; 
	      }
	    }
	  }
//#if POB_DEBUG
#if false
          Console.WriteLine("{0} left: {1}" , _node.Address, left);
#endif
	  if( left < _desired_neighbors ) {
            _need_left = 1;
	    return true;
	  } 
	  else {
            _need_left = 0;
	    return false;
	  }
	}
      }
    }

    /**
     * @returns true if we have too few right neighbor connections
     */
    protected bool NeedRightNeighbor {
      get {
	lock( _sync ) {
          if( _need_right != -1 ) {
            return (_need_right == 1);
	  }
	  int right = 0;
	  AHAddress local = (AHAddress)_node.Address;
	  ConnectionTable tab = _node.ConnectionTable;
	  lock( tab.SyncRoot ) {
            //foreach(Connection c in _node.ConnectionTable.GetConnections(struc_near)) {
            foreach(Connection c in tab.GetConnections(ConnectionType.Structured)) {
              AHAddress adr = (AHAddress)c.Address;
//#if POB_DEBUG
#if false
              Console.WriteLine("{0} -> {1}, ridx: {2}, is_right: {3}",
			    _node.Address, adr, RightPosition(adr), adr.IsRightOf( local) );
#endif
	      if(
	        //adr.IsRightOf( local ) &&
                (RightPosition( adr ) < _desired_neighbors) ) {
                //This is right neighbor:
	        right++; 
	      }
	    }
	  }
	  if( right < _desired_neighbors ) {
            _need_right = 1;
	    return true;
	  } 
	  else {
            _need_right = 0;
	    return false;
	  }
        }
      }
    }
    
    /**
     * @returns true if we have too few right shortcut connections
     */
    protected bool NeedShortcut {
      get {
	lock( _sync ) {
          if( _need_short != -1 ) {
            return (_need_short == 1);
	  }
	  int shortcuts = 0;
	  lock( _node.ConnectionTable.SyncRoot ) {
            foreach(Connection c in _node.ConnectionTable.GetConnections(struc_short)) {
              int left_pos = LeftPosition((AHAddress)c.Address);
              int right_pos = RightPosition((AHAddress)c.Address); 
	      if( left_pos >= _desired_neighbors &&
	          right_pos >= _desired_neighbors ) {
              /*
	       * No matter what they say, don't count them
	       * as a shortcut if they are one a close neighbor
	       */
                shortcuts++;
	      }
	    }
	  }
	  if( shortcuts < _desired_shortcuts ) {
            _need_short = 1;
	    return true;
	  } 
	  else {
            _need_short = 0;
	    return false;
	  }
	}
      }
    }
    
    ///////////////// Methods //////////////////////
    
    /**
     * Starts the Overlord if we are active
     *
     * This method is called by the CheckState method
     * IF we have not seen any connections in a while
     * AND we still need some connections
     *
     */
    public override void Activate()
    {
#if POB_DEBUG
      Console.WriteLine("In Activate: {0}", _node.Address);
#endif
      if( IsActive == false ) {
        return;
      }
      ConnectionTable tab = _node.ConnectionTable;
      //If we are going to connect to someone, this is how we
      //know who to use
      Address forwarder = null;
      Address target = null;
      short ttl = -1;
      string contype = "";
      int structured_count = 0;
      int leaf_count = 0;
      
      lock( tab.SyncRoot ) {
	leaf_count = tab.Count(ConnectionType.Leaf);
	if( leaf_count == 0 )
	{
          /*
	   * We first need to get a Leaf connection
	   */
          return;
	}
	structured_count = tab.Count(ConnectionType.Structured);

	if( structured_count < 2 ) {
          //We don't have enough connections to guarantee a connected
	  //graph.  Use a leaf connection to get another connection
	  Connection leaf = null;
	  do {
            leaf = tab.GetRandom(ConnectionType.Leaf);
	  }
	  while( leaf != null &&
	         tab.Count( ConnectionType.Leaf ) > 1 &&
		 tab.Contains( ConnectionType.Structured, leaf.Address ) );
	  //Now we have a random leaf that is not a
	  //structured neighbor to try to get a new neighbor with:
	  if( leaf != null ) {
	    target = GetSelfTarget();
	    if( ! target.Equals( leaf.Address ) ) {
	      //As long as the forwarder is not the target, forward
              forwarder = leaf.Address;
	    }
	    ttl = 1024;
	    //This is a near neighbor connection
	    contype = struc_near;
	  }
	}
      }//End of ConnectionTable lock
      if( target != null ) {
        if( forwarder != null ) {
          ForwardedConnectTo(forwarder, target, ttl, contype);
	}
	else {
          ConnectTo(target, ttl, contype);
	}
      }
      if( structured_count > 0 && target == null ) {
          //We have enough structured connections to ignore the leafs
          
	  /**
	   * We need left or right neighbors we send
	   * a ConnectToMessage in the directons we
	   * need.
	   */

	  bool trying_near = false;
          if( NeedLeftNeighbor ) {
#if POB_DEBUG
      Console.WriteLine("NeedLeftNeighbor: {0}", _node.Address);
#endif
            target = new DirectionalAddress(DirectionalAddress.Direction.Left);
	    ttl = (short)_desired_neighbors;
	    contype = struc_near;
	    trying_near = true;
            ConnectTo(target, ttl, contype);
          }
	  if( NeedRightNeighbor ) {
#if POB_DEBUG
      Console.WriteLine("NeedRightNeighbor: {0}", _node.Address);
#endif
            target = new DirectionalAddress(DirectionalAddress.Direction.Right);
	    ttl = (short)_desired_neighbors;
	    contype = struc_near;
	    trying_near = true;
            ConnectTo(target, ttl, contype);
          }
	  /*
	   * If we are trying to get near connections it
	   * is not smart to try to get a shortcut.  We
	   * need to make sure we are on the proper place in
	   * the ring before doing the below:
	   */
	  if( !trying_near && !_tried_shortcut && NeedShortcut ) {
#if POB_DEBUG
      Console.WriteLine("NeedShortcut: {0}", _node.Address);
#endif
            _tried_shortcut = true;
            target = GetShortcutTarget(); 
	    ttl = 1024;
	    contype = struc_short;
            ConnectTo(target, ttl, contype);
          }
      }
    }

    /**
     * Every heartbeat we take a look to see if we should trim
     *
     * We only trim one at a time.
     */
    protected void CheckState(object node, EventArgs eargs)
    {
#if POB_DEBUG
      Console.WriteLine("In Check for State");
#endif
      lock( _sync ) {
        if( IsActive == false ) {
          //If we are not active, we do not care what
	  //our state is.
          return;
        }
        TimeSpan elapsed = DateTime.Now - _last_connection_time;
	if( elapsed.TotalSeconds >= _trim_delay ) {
#if POB_DEBUG
          Console.WriteLine("Go for State check");
#endif
	  //else {
#if TRIM
            //We may be able to trim connections.
            ConnectionTable tab = _node.ConnectionTable;
	    int not_near = 0;
	    int nears = 0;
	    ArrayList sc_trim_candidates = new ArrayList();
	    ArrayList near_trim_candidates = new ArrayList();
	    lock( tab.SyncRoot ) {
	      
	      foreach(Connection c in tab.GetConnections(struc_short)) {
                int left_pos = LeftPosition((AHAddress)c.Address);
                int right_pos = RightPosition((AHAddress)c.Address); 
	        if( left_pos >= _desired_neighbors &&
	          right_pos >= _desired_neighbors ) {
	         /*
		  * Verify that this shortcut is not close
		  */
                  sc_trim_candidates.Add(c);
		}
	      }
	      foreach(Connection c in tab.GetConnections(struc_near)) {
                int right_pos = RightPosition((AHAddress)c.Address);
                int left_pos = LeftPosition((AHAddress)c.Address);
	    
	        if( right_pos > 2 * _desired_neighbors &&
		    left_pos > 2 * _desired_neighbors ) {
	          //These are near neighbors that are not so near
	          near_trim_candidates.Add(c);
		}
                
	      }
	    }//End of ConnectionTable lock

	    bool sc_needs_trim = (sc_trim_candidates.Count > 2 * _desired_shortcuts);
	    bool near_needs_trim = (near_trim_candidates.Count > 0);
	    /*
	     * Prefer to trim near neighbors that are unneeded, since
	     * they are not as useful for routing
	     * If there are no unneeded near neighbors, then
	     * consider trimming the shortcuts
	     */
	    if( near_needs_trim ) {
	      //Delete a farthest trim candidate:
        BigInteger biggest_distance = new BigInteger(0);
        BigInteger tmp_distance = new BigInteger(0);
        Connection to_trim = null;
        foreach(Connection tc in near_trim_candidates ) 
        {
          AHAddress t_ah_add = (AHAddress)tc.Address;
          tmp_distance = t_ah_add.DistanceTo( (AHAddress)_node.Address);
          if (tmp_distance > biggest_distance) {
            biggest_distance = tmp_distance;
            Console.WriteLine("...finding far distance for trim: {0}",biggest_distance.ToString() );
            to_trim = tc;
          }
        }
        Console.WriteLine("Final distance for trim{0}: ",biggest_distance.ToString() );
	      //Delete a random trim candidate:
	      //int idx = _rand.Next( near_trim_candidates.Count );
	      //Connection to_trim = (Connection)near_trim_candidates[idx];
#if POB_DEBUG
        Console.WriteLine("Attempt to trim Near: {0}", to_trim);
#endif
	      _node.GracefullyClose( to_trim.Edge );
	    }
	    else if( sc_needs_trim ) {
	      /**
	       * @todo use a better algorithm here, such as Nima's
	       * algorithm for biasing towards more distant nodes:
	       */
	      //Delete a random trim candidate:
	      int idx = _rand.Next( sc_trim_candidates.Count );
	      Connection to_trim = (Connection)sc_trim_candidates[idx];
#if POB_DEBUG
              Console.WriteLine("Attempt to trim Shortcut: {0}", to_trim);
#endif
	      _node.GracefullyClose( to_trim.Edge );
	    }
#endif
	  //}
	  if( NeedConnection ) {
            //Wake back up and try to get some
            Activate();
	  }
	}
      }
    }
    /**
     * This method is called when a new Connection is added
     * to the ConnectionTable
     */
    protected void ConnectHandler(object contab, EventArgs eargs)
    {
      //These are the two closest target addresses
      Address ltarget = null;
      Address rtarget = null;
	    
      lock( _sync ) {
        _last_connection_time = DateTime.Now;
        _need_left = -1;
        _need_right = -1;
        _need_short = -1;
      }
      if( IsActive == false ) {
        return;
      }
      ConnectionEventArgs args = (ConnectionEventArgs)eargs;
      Connection new_con = args.Connection;
      bool connect_left = false;
      bool connect_right = false;
      
      if( new_con.MainType == ConnectionType.Leaf ) {
	/*
	 * We just got a leaf.  Try to use it to get a shortcut.near
	 * This leaf could be connecting a new part of the network
	 * to us.  We try to connect to ourselves to make sure
	 * the network is connected:
	 */
	Address target = GetSelfTarget();
	short ttl = 1024;
	//This is a near neighbor connection
	string contype = struc_near;
        ForwardedConnectTo(new_con.Address, target, ttl, contype);
      }
      else if( new_con.MainType == ConnectionType.Structured ) {
       ConnectionTable tab = _node.ConnectionTable;
       lock( tab.SyncRoot ) {
        int left_pos = LeftPosition((AHAddress)new_con.Address);
        int right_pos = RightPosition((AHAddress)new_con.Address); 

	if( left_pos < _desired_neighbors ) {
        /*
	 * This is a new left neighbor.  Always
	 * connect to the right of a left neighbor,
	 * this will make sure we are connected to
	 * our local neighborhood.
	 */
          connect_right = true;
	  if( left_pos < _desired_neighbors - 1) { 
            /*
	     * Don't connect to the left of our most
	     * left neighbor.  If this is not our
	     * most left neighbor, make sure we are
	     * connected to his left
	     */
            connect_left = true;
	  }
	}
	if( right_pos < _desired_neighbors ) {
        /*
	 * This is a new right neighbor.  Always
	 * connect to the left of a right neighbor,
	 * this will make sure we are connected to
	 * our local neighborhood.
	 */
          connect_left = true;
	  if( right_pos < _desired_neighbors - 1) { 
            /*
	     * Don't connect to the right of our most
	     * right neighbor.  If this is not our
	     * most right neighbor, make sure we are
	     * connected to his right
	     */
            connect_right = true;
	  }
	}
	
	if( left_pos >= _desired_neighbors && right_pos >= _desired_neighbors ) {
        //This looks like a shortcut
	  
	  if( new_con.ConType == struc_short ) {
          //After we get a shortcut, we consider it possible again
	  //and are willing to try
            _tried_shortcut = false;	
	  }
	}

	/*
	 * Check to see if any of this node's neighbors
	 * should be neighbors of us.  If they should, connect
	 * to the closest such nodes on each side.
	 */
	BigInteger ldist = -1;
	BigInteger rdist = -1;
	AHAddress local = (AHAddress)_node.Address;
	foreach(NodeInfo ni in new_con.Status.Neighbors) {
	  if( !tab.Contains(ConnectionType.Structured, ni.Address) ) {
	    AHAddress adr = (AHAddress)ni.Address;
            int n_left = LeftPosition( adr );
            int n_right = RightPosition( adr );
	    if( left_pos < _desired_neighbors || right_pos < _desired_neighbors ) {
            //We should connect to this node! if we are not already:
	      BigInteger adr_dist = local.LeftDistanceTo(adr);
	      if( adr_dist < ldist || ldist == -1 ) {
                ldist = adr_dist;
		ltarget = adr;
	      }
	      adr_dist = local.RightDistanceTo(adr);
	      if( adr_dist < rdist || rdist == -1 ) {
                rdist = adr_dist;
		rtarget = adr;
	      }
	    }
	  }
	}//We have looked at all his neighbors
       }
      }//Release the lock on the connection_table
      
      /* 
       * We want to make sure not to hold the lock on ConnectionTable
       * while we try to make new connections
       */
      short f_ttl = 3; //2 would be enough, but 1 extra...
      if( rtarget != null ) {
        ForwardedConnectTo(new_con.Address, rtarget, f_ttl, struc_near);
      }
      if( ltarget != null ) {
        ForwardedConnectTo(new_con.Address, ltarget, f_ttl, struc_near);
      }
      //We also send directional messages.  In the future we may find this
      //to be unnecessary
      ///@todo evaluate the performance impact of this:

      if( rtarget == null || ltarget == null ) {
      /**
       * Once we find nodes for which we can't get any closer, we
       * make sure we are connected to the right and left of that node.
       *
       * When we connect to a neighbor's neighbor with directional addresses
       * we need TTL = 2.  1 to get to the neighbor, 2 to get to the neighbor's
       * neighbor.
       */
        short nn_ttl = 2;
        if( connect_right ) {
          ConnectToOnEdge(new DirectionalAddress(DirectionalAddress.Direction.Right),
                        new_con.Edge, nn_ttl, struc_near); 
        }
        if( connect_left ) {
          ConnectToOnEdge(new DirectionalAddress(DirectionalAddress.Direction.Left),
                        new_con.Edge, nn_ttl, struc_near); 
        }
      }
    }

    /**
     * When a Connector finishes his job, this method is called to
     * clean up
     */
    protected void ConnectorEndHandler(object connector, EventArgs args)
    {
      lock( _sync ) {
        _connectors.Remove(connector);
      }
    }

    /**
     * A helper function that handles the making Connectors
     * and setting up the ConnectToMessage
     */
    protected void ConnectTo(Address target, short t_ttl, string contype)
    {
      ConnectToOnEdge(target, _node, t_ttl, contype);
    }

    /**
     * A helper function that handles the making Connectors
     * and setting up the ConnectToMessage
     *
     * This returns immediately if we are already connected
     * to this node.
     */
    protected void ConnectToOnEdge(Address target, IPacketSender edge,
		                   short t_ttl, string contype)
    {
      //If we already have a connection to this node,
      //don't try to get another one, it is a waste of 
      //time.
      ConnectionType mt = Connection.StringToMainType(contype);
      if( _node.ConnectionTable.Contains( mt, target ) ) {
        return; 
      }
      short t_hops = 0;
      if( edge is Edge ) {
	/*
	 * In this case we are bypassing the router and sending
	 * having the Connector talk directly to the neighbor
	 * using the edge.  We need to go ahead an increment
	 * the hops of the packet in order to make it the case
	 * the the packet has taken 1 hop by the time it arrives
	 */
        t_hops = 1;
      }
      ConnectToMessage ctm =
        new ConnectToMessage(contype, new NodeInfo(_node.Address, _node.LocalTAs) );
      ctm.Id = _rand.Next(1, Int32.MaxValue);
      ctm.Dir = ConnectionMessage.Direction.Request;

      AHPacket ctm_pack =
        new AHPacket(t_hops, t_ttl, _node.Address, target,
                     AHPacket.Protocol.Connection, ctm.ToByteArray());

      #if DEBUG
      System.Console.WriteLine("In ConnectToOnEdge:");
      System.Console.WriteLine("Local:{0}", _node.Address);
      System.Console.WriteLine("Target:{0}", target);
      System.Console.WriteLine("Message ID:{0}", ctm.Id);
      #endif

      Connector con = new Connector(_node);
      //Keep a reference to it does not go out of scope
      lock( _sync ) {
        _connectors.Add(con);
      }
      con.FinishEvent += new EventHandler(this.ConnectorEndHandler);
      con.Connect(edge, ctm_pack, ctm.Id);
#if PLAB_CTM_LOG
      this.Logger.LogCTMEvent(target);
#endif
    }
    
    /**
     * This method is called when there is a Disconnection from
     * the ConnectionTable
     */
    protected void DisconnectHandler(object connectiontable, EventArgs args)
    { 
      lock( _sync ) {
        _last_connection_time = DateTime.Now;
        _need_left = -1;
        _need_right = -1;
        _need_short = -1;
      }
      Connection c = ((ConnectionEventArgs)args).Connection;

      if( IsActive ) {
        if( c.MainType == ConnectionType.Structured ) {
          int right_pos = RightPosition((AHAddress)c.Address);
          int left_pos = LeftPosition((AHAddress)c.Address);
	  if( right_pos < _desired_neighbors ) {
            //We lost a close friend.
            Address target = new DirectionalAddress(DirectionalAddress.Direction.Right);
	    short ttl = (short)_desired_neighbors;
	    string contype = struc_near;
            ConnectTo(target, ttl, contype);
	  }
	  if( left_pos < _desired_neighbors ) {
            //We lost a close friend.
            Address target = new DirectionalAddress(DirectionalAddress.Direction.Left);
	    short ttl = (short)_desired_neighbors;
	    string contype = struc_near;
            ConnectTo(target, ttl, contype);
	  }
          if( c.ConType == struc_short ) {
            if( NeedShortcut ) {
              Address target = GetShortcutTarget(); 
	      short ttl = 1024;
	      string contype = struc_short;
              ConnectTo(target, ttl, contype);
	    }
          }
        }
        else {
          //Just activate and see what happens:
	  Activate();
        }
      }
    }
    
    /**
     * This is a helper function.
     */
    protected void ForwardedConnectTo(Address forwarder,
                                      Address target,
                                      short t_ttl, string contype)
    {
      //If we already have a connection to this node,
      //don't try to get another one, it is a waste of 
      //time.
      ConnectionType mt = Connection.StringToMainType(contype);
      if( _node.ConnectionTable.Contains( mt, target ) ) {
        return; 
      }
      ConnectToMessage ctm = new ConnectToMessage(contype,
                              new NodeInfo( _node.Address, _node.LocalTAs) );
      ctm.Dir = ConnectionMessage.Direction.Request;
      ctm.Id = _rand.Next(1, Int32.MaxValue);
      short t_hops = 0;
      //This is the packet we wish we could send: local -> target
      AHPacket ctm_pack = new AHPacket(t_hops,
                                       t_ttl,
                                       _node.Address,
                                       target, AHPacket.Protocol.Connection,
                                       ctm.ToByteArray() );
      //We now have a packet that goes from local->forwarder, forwarder->target
      AHPacket forward_pack = PacketForwarder.WrapPacket(forwarder, 1, ctm_pack);

      #if DEBUG
      System.Console.WriteLine("In ForwardedConnectTo:");
      System.Console.WriteLine("Local:{0}", _node.Address);
      System.Console.WriteLine("Target:{0}", target);
      System.Console.WriteLine("Message ID:{0}", ctm.Id);
      #endif

      Connector con = new Connector(_node);
      //Keep a reference to it does not go out of scope
      lock( _sync ) {
        _connectors.Add(con);
      }
      con.FinishEvent += new EventHandler(this.ConnectorEndHandler);
      con.Connect(forward_pack, ctm.Id);
#if PLAB_CTM_LOG
      this.Logger.LogCTMEvent(target);
#endif

    }

    /**
     * When we want to connect to the address closest
     * to us, we use this address.
     */
    protected Address GetSelfTarget()
    {
      /**
       * try to get at least one neighbor using forwarding through the 
       * leaf .  The forwarded address is 2 larger than the address of
       * the new node that is getting connected.
       */
      BigInteger local_int_add = _node.Address.ToBigInteger();
      //must have even addresses so increment twice
      local_int_add += 2;
      //Make sure we don't overflow:
      BigInteger tbi = new BigInteger(local_int_add % Address.Full);
      return new AHAddress(tbi);
    }

    /**
     * Return a random shortcut target with the
     * correct distance distribution
     */
    protected Address GetShortcutTarget()
    {
      // Random distance from 2^1 - 2^159 (1/d distributed)
      int rand_exponent = _rand.Next(1, 159);
      BigInteger rand_dist = new BigInteger(2);
      rand_dist <<= (rand_exponent - 1);

      // Add or subtract random distance to the current address
      BigInteger t_add = _node.Address.ToBigInteger();

      // Random number that is 0 or 1
      if( _rand.Next(2) == 0 ) {
        t_add += rand_dist;
      }
      else {
        t_add -= rand_dist;
      }

      BigInteger target_int = new BigInteger(t_add % Address.Full);

      return new AHAddress(target_int); 
    }
    
    /**
     * Given an address, we see how many of our connections
     * are closer than this address to the left
     */
    protected int LeftPosition(AHAddress addr)
    {
      AHAddress local = (AHAddress)_node.Address;
      BigInteger addr_dist = local.LeftDistanceTo(addr);
      //Don't let the Table change while we do this:
      ConnectionTable tab = _node.ConnectionTable;
      int closer_count = 0;
      lock( tab.SyncRoot ) {
        foreach(Connection c in tab.GetConnections(ConnectionType.Structured)) {
          AHAddress c_addr = (AHAddress)c.Address;
	  if( local.LeftDistanceTo( c_addr ) < addr_dist ) {
            closer_count++;
	  }
	}
      }
      return closer_count;
    }
    /**
     * Given an address, we see how many of our connections
     * are closer than this address to the right.
     */
    protected int RightPosition(AHAddress addr)
    {
      AHAddress local = (AHAddress)_node.Address;
      BigInteger addr_dist = local.RightDistanceTo(addr);
      //Don't let the Table change while we do this:
      ConnectionTable tab = _node.ConnectionTable;
      int closer_count = 0;
      lock( tab.SyncRoot ) {
        foreach(Connection c in tab.GetConnections(ConnectionType.Structured)) {
          AHAddress c_addr = (AHAddress)c.Address;
	  if( local.RightDistanceTo( c_addr ) < addr_dist ) {
            closer_count++;
	  }
	}
      }
      return closer_count;
    }
    
  }

}