/*
This program is part of BruNet, a library for the creation of efficient overlay
networks.
Copyright (C) 2005-2006  University of Florida

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using Brunet.Util;
using Brunet.Connections;
using Brunet.Transport;

using Brunet.Messaging;
namespace Brunet.Symphony {
  public class NodeRankComparer : System.Collections.IComparer {
    public int Compare(object x, object y) {
      if( x == y ) {
        //This is trivial, but we need to deal with it:
        return 0;
      }
      NodeRankInformation x1 = (NodeRankInformation) x;
      NodeRankInformation y1 = (NodeRankInformation) y;
      if (x1.Equals(y1) && x1.Count == y1.Count) {
        /*
         * Since each Address is in our list at most once,
         * this is an Error, so lets print it out and hope
         * someone sees it.
         */
	      return 0;
      } else if (x1.Count <= y1.Count) {
	      return 1;
      } else if (x1.Count > y1.Count) {
	      return -1;
      }
      return -1;
    }
  }

  public class NodeRankInformation { 
    //address of the node
    private Address _addr;
    //rank - score is a better name though
    private int _count;
    
    //constructor
    public NodeRankInformation(Address addr) {
      _addr = addr;
      _count = 0;
    }
    public int Count {
      get { return _count; }
      set { _count = value; }
    }

    public Address Addr { get { return _addr; } }

    override public bool Equals(Object other ) {
      if( Object.ReferenceEquals(other, this) ) {
        return true;
      }

      NodeRankInformation other1 =  other as NodeRankInformation;
      if( Object.ReferenceEquals(other1, null)) {
        return false;
      } else if (_addr.Equals(other1.Addr)) {
	      return true;
      }
      return false;
    }

    override public int GetHashCode() {
      // This should be safe, we shouldn't have more than one per Address.
      return _addr.GetHashCode();
    }

    override public string ToString() {
      return _addr.ToString() + ":" + _count;
    }
  }

  /** The following is what we call a ChotaConnectionOverlord.
   *  This provides high-performance routing by setting up direct
   *  structured connections between pairs of highly communicating nodes.
   *  Chota - in Hindi means small. 
   */
  public class ChotaConnectionOverlord : ConnectionOverlord {
    //used for locking
    protected object _sync;
    //our random number generator
    protected Random _rand;

    //if the overlord is active
    protected bool _active;
    
    //minimum score before we start forming chota connections
//    private static readonly int MIN_SCORE_THRESHOLD = SAMPLE_SIZE + 1;
    public const int MIN_SCORE_THRESHOLD = 1;

    //the maximum number of Chota connections we plan to support
    private static readonly int MAX_CHOTA = 200;
    
    //hashtable of destinations. for each destination we maintain 
    //how frequently we communicate with it. Just like the LRU in virtual
    // memory context - Arijit Ganguly. 
    protected ArrayList _node_rank_list;
    /*
     * Allows us to quickly look up the node rank for a destination
     */
    protected Hashtable _dest_to_node_rank;

    //node rank comparer
    protected NodeRankComparer _cmp;

    protected static readonly int SAMPLE_SIZE = 4;
    
    /*
     * We don't want to risk mistyping these strings.
     */
    static protected readonly string struc_chota = "structured.chota";

    public override TAAuthorizer TAAuth { get { return _ta_auth;} }
    protected readonly static TAAuthorizer _ta_auth = new TATypeAuthorizer(
          new TransportAddress.TAType[]{TransportAddress.TAType.Subring},
          TAAuthorizer.Decision.Deny,
          TAAuthorizer.Decision.None);

    public ChotaConnectionOverlord(Node n)
    {
      _node = n;
      _cmp = new NodeRankComparer();
      _sync = new object();
      _rand = new Random();
      _node_rank_list = new ArrayList();
      _dest_to_node_rank = new Hashtable();

      lock( _sync ) {
      	// we assess trimming/growing situation on every heart beat
        _node.HeartBeatEvent += this.CheckState;
      }
    }

    /**
     * On every activation, the ChotaConnectionOverlord trims any connections
     * that are unused, and also creates any new connections of needed
     */
    override public void Activate() {
      if(!_active) {
        return;
      }

      ConnectionList cons = _node.ConnectionTable.GetConnections(Connection.StringToMainType(struc_chota));

      // Trim and add OUTSIDE of the lock!
      var to_trim = new List<Connection>();
      List<Address> to_add = new List<Address>();

      lock(_sync) {
        _node_rank_list.Sort( _cmp );
        // Find the guys to trim....
        for (int i = _node_rank_list.Count - 1; i >= MAX_CHOTA && i > 0; i--) {
          NodeRankInformation node_rank = (NodeRankInformation) _node_rank_list[i];
          // Must remove from _dest_to_node_rank to prevent memory leak
          _dest_to_node_rank.Remove(node_rank.Addr);
          // Now check to see if ChotaCO owns this connections and add to_trim if it does
          int idx = cons.IndexOf(node_rank.Addr);
          if(idx >= 0 && cons[idx].ConType.Equals(struc_chota)) {
            to_trim.Add(cons[idx]);
          }
        }

        // Don't keep around stale state
        if(_node_rank_list.Count > MAX_CHOTA) {
          _node_rank_list.RemoveRange(MAX_CHOTA, _node_rank_list.Count - MAX_CHOTA);
        }

        // Find guys to connect to!
        for (int i = 0; i < _node_rank_list.Count && i < MAX_CHOTA; i++) {
          //we are traversing the list in descending order of 
          NodeRankInformation node_rank = (NodeRankInformation) _node_rank_list[i];
          if (node_rank.Count < MIN_SCORE_THRESHOLD ) {
            //too low score to create a connection
            continue;
          } else if(cons.IndexOf(node_rank.Addr) >= 0) {
            // already have a connection to that node!
            continue;
          }
          to_add.Add(node_rank.Addr);
        }
      }

      foreach(Connection c in to_trim) {
        _node.GracefullyClose(c.State.Edge, "From Chota, low score trim.");
      }

      foreach(Address addr in to_add) {
	      ConnectTo(addr, struc_chota);
      }
    }
    
    override public bool NeedConnection { get { return true; } }

    public override bool IsActive 
    {
      get { return _active; }
      set { _active = value; }
    }

    public void Increment(Address dest) {
      // Sample every 1 / SAMPLE_SIZE
      if( _rand.Next(SAMPLE_SIZE) != 0 ) {
        return;
      }

      lock(_sync) {
        NodeRankInformation node_rank =
          (NodeRankInformation) _dest_to_node_rank[dest];
        if( node_rank == null ) {
          node_rank = new NodeRankInformation(dest);
          _node_rank_list.Add( node_rank );
          _dest_to_node_rank[dest] = node_rank;
        }
        // Increment by SAMPLE_SIZE
        node_rank.Count += SAMPLE_SIZE;
      }
    }

    /**
     * On every heartbeat this method is invoked.
     * Sort the table, decrement node rank, and run Activate.
     */
    public void CheckState(object node, EventArgs eargs) {
      if(!_active) {
        return;
      }

      if( _rand.Next(SAMPLE_SIZE) != 0 ) {
        return;
      }

    	lock(_sync) { //lock the score table
        foreach(NodeRankInformation node_rank in _node_rank_list) {
          node_rank.Count = (node_rank.Count > SAMPLE_SIZE) ? node_rank.Count - SAMPLE_SIZE : 0;
        }
      }

      Activate();
    }
  }
}