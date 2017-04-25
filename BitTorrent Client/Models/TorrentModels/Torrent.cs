﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Threading;

using BitTorrent_Client.Models.Bencoding;
using BitTorrent_Client.Models.TrackerModels;
using BitTorrent_Client.Models.PeerModels;
using BitTorrent_Client.Models.Utility_Functions;

namespace BitTorrent_Client.Models.TorrentModels
{
    public class Torrent : INotifyPropertyChanged
    {
        #region Fields
        
        // Stores the announce url's for all trackers parsed from .torrent file.
        List<string> m_announceList = new List<string>();


        private Dictionary<int, int> m_downloadOrder = new Dictionary<int, int>();

        private Dictionary<int, int> m_pieceOccurances = new Dictionary<int, int>();

        private DateTime m_lastUpdate;
       
        // Stores if piece has been verified.
        private bool[] m_verifiedPieces;

        // Stores the blocks that have been downloaded.
        private bool[][] m_haveBlocks;
        // Stores the blocks that have been requested.
        private bool[][] m_requestedBlocks;

        // Stores if there are multiple files in torrent.
        private bool m_singleFile;
       
        // Stores the sha1 hashes for each piece.
        private byte[] m_pieces;
        // Stores the raw byte data of torrent.
        private byte[] m_rawData;

        // Stores the max number blocks to request from each peer at a time.
        private int m_maxPeerRequest;

        private float m_currentProgress;

        // Stores if the torrent is a private tracker.        
        private long m_privateTracker;

        private long m_downloadedSince;

        private long m_downloaded;

        private string m_downloadSpeed;

        private string m_status;
        #endregion

        #region Constructors

        public Torrent()
        {
            // Initialize class objects.
            Leechers = new ConcurrentDictionary<string, Peer>();
            Peers = new ConcurrentDictionary<string, Peer>();
            Seeders = new ConcurrentDictionary<string, Peer>();
            Files = new ObservableCollection<FileWrapper>();
            IncomingBlocks = new ConcurrentQueue<IncomingBlock>();
            Trackers = new ObservableCollection<Tracker>();
        }

        #endregion

        #region Events

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string a_propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(a_propertyName));
        }

        private void HandleBitfieldRecieved(object a_peer, EventArgs a_args)
        {
            var peer = a_peer as Peer;
            for(var i = 0; i < NumberOfPieces; i++)
            {
                m_pieceOccurances[i]++;
            }
        }
        private void HandleBlockCanceled(object a_peer, OutgoingBlock a_block)
        {

        }

        private void HandleBlockReceived(object a_peer, IncomingBlock a_block)
        {
            IncomingBlocks.Enqueue(a_block);
        }

        private void HandleBlockRequested(object a_peer, OutgoingBlock a_block)
        {

        }

        private void HandleHaveReceived(object a_peer, int a_pieceIndex)
        {
            m_pieceOccurances[a_pieceIndex]++;
        }
        private void HandlePeerDisconnected(object a_peer, EventArgs a_args)
        {
            var peer = (Peer)a_peer;
            peer.BlockCanceled -= HandleBlockCanceled;
            peer.BlockReceived -= HandleBlockReceived;
            peer.BlockRequested -= HandleBlockRequested;
            peer.Disconnected -= HandlePeerDisconnected;

            Peer peerDisconnect;

            if (Peers.TryRemove(peer.IP, out peerDisconnect))
            {
                Console.WriteLine("Peer {0}:{1} was removed from the peers list", peerDisconnect.IP, peerDisconnect.Port);
            }
            if(Seeders.TryRemove(peer.IP, out peerDisconnect))
            {
                //Console.WriteLine("Peer {0}:{1} was removed from the seeders list", peerDisconnect.IP, peerDisconnect.Port);

            }
        }

        private void HandlePeerListUpdated(object a_sender, List<string> a_addresses)
        {
            foreach (string address in a_addresses)
            {
                SetPeerEventHandlers(new Peer(this, address));
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Get/Private set the creation date of the torrent.
        /// </summary>
        public DateTime CreationDate
        {
            get;
            private set;
        }

        /// <summary>
        /// Get/Private set all of the peers that the client is currently uploading to.
        /// </summary>
        public ConcurrentDictionary<string, Peer> Leechers
        {
            get;
            private set;
        }
        /// <summary>
        /// Get/Private set all of peers that the client is connected to.
        /// </summary>
        public ConcurrentDictionary<string, Peer> Peers
        {
            get;
            private set;
        }

        /// <summary>
        /// Get/Private set all of the peers that the client is currently downloading from.
        /// </summary>
        public ConcurrentDictionary<string, Peer> Seeders
        {
            get;
            private set;
        }
        /// <summary>
        /// Get/Private set all of the incomming blocks that are awaiting processing.
        /// </summary>
        public ConcurrentQueue<IncomingBlock> IncomingBlocks
        {
            get;
            private set;
        }

        /// <summary>
        /// Get/Private set all of the trackers being used.
        /// </summary>
        public ObservableCollection<Tracker> Trackers
        {
            get;
            private set;
        }

        /// <summary>
        /// Get/Private set of an observable collection that holds all files
        /// in the torrent.
        /// </summary>
        public ObservableCollection<FileWrapper> Files
        {
            get;
            private set;
        }

        public bool[][] HaveBlocks
        {
            get { return m_haveBlocks; }
            set { m_haveBlocks = value; }
        }

        /// <summary>
        /// Stores if all the files have been downloaded.
        /// </summary>
        public bool Complete
        {
            get;
            private set;
        }

        /// <summary>
        /// Get/Set of a bool that indicates whether the torrent has started 
        /// network communications.
        /// </summary>
        public bool Started
        {
            get;
            set;
        }


        /// <summary>
        /// Get/Private set of the sha1 hash of the torrent's info dictionary.
        /// </summary>
        public byte[] ByteInfoHash
        {
            get;
            private set;
        }

        
        /// <summary>
        /// Gets the default block length.
        /// </summary>
        public int BlockLength
        {
            get { return 16384; }
        }


        /// <summary>
        /// Gets the number of pieces that makeup the file(s).
        /// </summary>
        public int NumberOfPieces
        {
            get
            {
                return (int)Math.Ceiling(Length / (double)PieceLength);
            }
        }

        public float CurrentProgress
        {
            get { return m_currentProgress; }
            set
            {
                if(m_currentProgress != value)
                {
                    m_currentProgress = value;
                    OnPropertyChanged("CurrentProgress");
                }
            }
        }

        /// <summary>
        /// Get/Private set the Length of the torrent.
        /// </summary>
        public long Length
        {
            get;
            private set;
        }

        /// <summary>
        /// Get/Private set the length of each piece.
        /// </summary>
        public long PieceLength
        {
            get;
            private set;
        }

        /// <summary>
        /// Get/Private set the included comment in the torrent.
        /// </summary>
        public string Comment
        {
            get;
            private set;
        }

        /// <summary>
        /// Get/Private set the name of the program that created the torrent.
        /// </summary>
        public string CreatedBy
        {
            get;
            private set;
        }

        public string DownloadSpeed
        {
           get { return m_downloadSpeed; }
            set
            {
                if (m_downloadSpeed != value)
                {
                    m_downloadSpeed = value;
                    OnPropertyChanged("DownloadSpeed");
                }
            }
        }

        /// <summary>
        /// Get/Private set the encoding of the torrent file.
        /// </summary>
        public string Encoding
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets a readable filesize for all the files of the torrent.
        /// </summary>
        public string FileSize
        {
            get
            {
                return Utility.GetBytesReadable(Length);
            }
        }

        /// <summary>
        /// Gets a string formatted sha1 hash of the info dictionary.
        /// </summary>
        public string InfoHash
        {
            get
            {
                return BitConverter.ToString(ByteInfoHash).Replace("-", "");
            }
        }

        /// <summary>
        /// Get/Private set the name of the file.
        /// </summary>
        public string Name
        {
            get;
            private set;
        }

        /// <summary>
        /// Get/Set the save directory of the torrent.
        /// </summary>
        public string SaveDirectory
        {
            get;
            set;
        }

        public string Status
        {
            get { return m_status; }
            set
            {
                if(m_status != value)
                {
                    m_status = value;
                    OnPropertyChanged("Status");

                }
            }
        }
        /// <summary>
        /// Get/Set the name of the torrent.
        /// </summary>
        public string TorrentName
        {
            get;
            set;
        }

        /// <summary>
        /// Get/Set the path of the torrent file.
        /// </summary>
        public string TorrentPath
        {
            get;
            set;
        }

        #endregion

        #region Methods

        #region Public Methods

        public void ComputeDownloadSpeed()
        {
            foreach(var peer in Peers)
            {
                if(peer.Value.LastUpdate == null)
                {
                    peer.Value.LastUpdate = DateTime.Now;
                    peer.Value.DownloadedSince = peer.Value.Downloaded;
                }
                else
                {
                    var peerTimeSpan = DateTime.Now - peer.Value.LastUpdate;
                    if(peerTimeSpan.Seconds > 2)
                    {
                        var peerByteChange = peer.Value.Downloaded - peer.Value.DownloadedSince;
                        var peerBytesPerSecond = peerByteChange / peerTimeSpan.Seconds;
                        peer.Value.DownloadedSince = peer.Value.Downloaded;
                        peer.Value.DownloadSpeed = Utility.GetBitsReadable(peerBytesPerSecond) + "/s";
                        peer.Value.LastUpdate = DateTime.Now;
                    }
                }

            }
            if (m_lastUpdate == null)
            {
                m_lastUpdate = DateTime.Now;
                m_downloadedSince = m_downloaded;
                return;
            }

            var timeSpan = DateTime.Now - m_lastUpdate;
            if(timeSpan.Seconds > 2)
            {
                var byteChange = m_downloaded - m_downloadedSince;
                var bytesPerSecond = byteChange / timeSpan.Seconds;
                m_downloadedSince = m_downloaded;
                DownloadSpeed = Utility.GetBytesReadable(bytesPerSecond) + "/s";
                m_lastUpdate = DateTime.Now;
            }
        }

        public void EndGameRequestBlocks()
        {
            Console.WriteLine("Endgame request");
            foreach (var peer in Seeders)
            {
                ComputeRarestPieces();
                foreach (var piece in m_downloadOrder)
                {
                    // If we already have a verified piece or if the peer does
                    // not have the piece.
                    if (m_verifiedPieces[piece.Key] || !peer.Value.HasPiece[piece.Key])
                    {
                        continue;
                    }

                    for (var block = 0; block < ComputeNumberOfBlocks(piece.Key); block++)
                    {
                        if (m_haveBlocks[piece.Key][block])
                        {
                            continue;
                        }
                       
                        if (peer.Value.NumberOfBlocksRequested > m_maxPeerRequest)
                        {
                            continue;
                        }

                        var blockLength = ComputeBlockLength(piece.Key, block);
                        peer.Value.SendRequest(piece.Key, block * blockLength, blockLength);
                        m_requestedBlocks[piece.Key][block] = true;

                        peer.Value.NumberOfBlocksRequested++;
                    }
                }
            }
        }
    
        public void ResumeDownloading()
        {
            foreach(var tracker in Trackers)
            {
                foreach(var address in tracker.Peers)
                {
                    SetPeerEventHandlers(new Peer(this, address));
                }
            }
        }

        public void PausePeers()
        {
            Started = false;

            foreach(var peer in Peers)
            {
                peer.Value.Disconnect();
            }

        }

        /// <summary>
        /// Opens a torrent file and stores bytes in byte array.
        /// </summary>
        /// <param name="a_path">The path of the torrent file</param>
        /// <remarks>
        /// OpenTorrent()
        /// 
        /// SYNOPSIS
        /// 
        ///     void OpenTorrent(string a_path);
        /// 
        /// DESCRIPTION
        /// 
        ///     This function will open the file located at the path a_path and
        ///     save the bytes in m_rawData to be decoded with the DecodeTorrent
        ///     function.
        /// </remarks>
        public void OpenTorrent(string a_path)
        {
            // Reads in file.
            m_rawData = TorrentIO.OpenTorrent(a_path);

            // Decodes the raw data from file.
            DecodeTorrent();

            foreach (var trackerUrl in m_announceList)
            {
                if (trackerUrl.Contains("ipv6"))
                {

                }

                else if (trackerUrl.Contains("http://"))
                {
                    Tracker tracker = new HttpTracker(this, trackerUrl);
                    Trackers.Add(tracker);
                    tracker.PeerListUpdated += HandlePeerListUpdated;
                }
                else if (trackerUrl.Contains("udp://"))
                {
                    //Tracker tracker = new UdpTracker(this, trackerUrl);
                    //Trackers.Add(tracker);
                    //tracker.PeerListUpdated += HandlePeerListUpdated;
                }
                
            }

            m_maxPeerRequest = ComputeNumberOfBlocks(0);
        }

        /// <summary>
        /// Processes any incoming blocks that are in the queue.
        /// </summary>
        /// <remarks>
        /// ProcessBlocks()
        /// 
        /// SYNOPSIS
        /// 
        ///     ProcessBlocks();
        ///     
        /// DESCRIPTION
        /// 
        ///     This function will process any incoming blocks that are in the
        ///     queue. It will first mark the block as have and if we have the
        ///     whole piece and the piece has not been verified already, then 
        ///     we try to verify the piece.
        /// </remarks>
        public void ProcessBlocks()
        {
            IncomingBlock block;
            while (IncomingBlocks.TryDequeue(out block))
            {

                var blockNumber = block.Begin / BlockLength;
                var index = block.Index;
                //Console.WriteLine("Received piece {0} block {1}", index, blockNumber);
                // Write the block to file.
                TorrentIO.WriteBlock(block, this);

                block.Peer.Downloaded += block.Block.Length;

                // Decrement the number of blocks requested for the peer.
                block.Peer.NumberOfBlocksRequested--;



                // When none of thet blocks in the piece are missing and piece is not verified.
                if (!m_haveBlocks[index].OfType<bool>().Contains(false) && !m_verifiedPieces[index])
                {
                    if (VerifyPiece(index))
                    {
                        Console.WriteLine("Piece {0} verified", index);

                        m_downloaded += ComputePieceLength(index);

                        CurrentProgress = (float)m_downloaded / Length;
                        m_verifiedPieces[index] = true;
                        if (!m_verifiedPieces.OfType<bool>().Contains(false))
                        {
                            Complete = true;
                            m_downloaded = Length;
                            CurrentProgress = 1;

                            while (IncomingBlocks.TryDequeue(out block))
                            {

                            }
                            return;
                        }

                        foreach(var peer in Peers)
                        {
                            if(!peer.Value.HandshakeReceived || !peer.Value.HandshakeSent)
                            {
                                continue;
                            }

                            if(peer.Value.Complete)
                            {
                                continue;
                            }
                            peer.Value.SendHave(index);

                        }
                    }
                    else
                    {
                        // Clear array and mark as unrequested when piece hash does not match.
                        Array.Clear(m_haveBlocks[index], 0, m_haveBlocks[index].Length);
                        m_requestedBlocks[index] = m_haveBlocks[index];
                    }
                }
            }
        }

        public void RequestBlocks()
        {
            foreach (var peer in Seeders)
            {
                ComputeRarestPieces();
                foreach(var piece in m_downloadOrder)
                { 
                    // If we already have a verified piece or if the peer does
                    // not have the piece.
                    if (m_verifiedPieces[piece.Key] || !peer.Value.HasPiece[piece.Key])
                    {
                        continue;
                    }

                    for (var block = 0; block < ComputeNumberOfBlocks(piece.Key); block++)
                    {
                        if (m_haveBlocks[piece.Key][block])
                        {
                            continue;
                        }
                        // If we already requested the block from some peer.
                        if (m_requestedBlocks[piece.Key][block])
                        {
                            continue;
                        }

                        if (peer.Value.NumberOfBlocksRequested > m_maxPeerRequest)
                        {
                            continue;
                        }

                        var blockLength = ComputeBlockLength(piece.Key, block);
                        peer.Value.SendRequest(piece.Key, block * blockLength, blockLength);
                        m_requestedBlocks[piece.Key][block] = true;

                        peer.Value.NumberOfBlocksRequested++;
                    }
                }
            }
        }

        public void UpdatePeers()
        {
     
            foreach (var peer in Peers)
            {
                // If a handshake has been send and received.
                if (!peer.Value.HandshakeReceived || !peer.Value.HandshakeSent)
                {
                    continue;
                }

                // If client has completed downloaded.
                if (Complete)
                {
                    // Send we are not interested to the peer.
                    peer.Value.SendNotInterested();

                    // When the client and peer have completely downloaded.
                    if (peer.Value.Complete)
                    {
                        peer.Value.Disconnect();
                    }
                }
                // When client has not completed downloading.
                else
                {
                    // If an interested message has not been sent, send one.
                    if (!peer.Value.AmInterested)
                    {
                        peer.Value.SendInterested();
                    }

                    // Send a keep alive message to avoid timeout.
                    peer.Value.SendKeepAliveMessage();

                    if (Started && Leechers.Count < 5)
                    {
                        if (peer.Value.PeerInterested && peer.Value.AmChoking)
                        {
                            peer.Value.SendUnchoke();
                        }
                    }

                    if (Started && Seeders.Count < 50)
                    {
                        if (!peer.Value.PeerChoking)
                        {
                            Seeders.TryAdd(peer.Key, peer.Value);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Updates torrent trackers.
        /// </summary>
        /// <remarks>
        /// UpdateTrackers()
        /// 
        /// SYNOPSIS
        /// 
        ///     void UpdateTrackers();
        ///     
        /// DESCRIPTION
        /// 
        ///     This function will go through every tracker in the Trackers collection
        ///     and call the update function.
        /// </remarks>
        public void UpdateTrackers()
        {
            foreach(Tracker tracker in Trackers)
            {
                Console.WriteLine("Updating {0}", tracker.TrackerUrl);
                tracker.Update();
            }
        }

        public void VerifyTorrent()
        {
            m_haveBlocks = new bool[NumberOfPieces][];
            m_verifiedPieces = new bool[NumberOfPieces];

            for (var i = 0; i < NumberOfPieces; i++)
            {
                CurrentProgress = (float)i / NumberOfPieces;
                m_haveBlocks[i] = new bool[ComputeNumberOfBlocks(i)];
                if (File.Exists(SaveDirectory + "\\" + Name))
                {
                    if (VerifyPiece(i))
                    {
                        Status = "Verifying";
                        m_verifiedPieces[i] = true;
                        for (var j = 0; j < ComputeNumberOfBlocks(i); j++)
                        {
                            m_haveBlocks[i][j] = true;
                        }

                    }
                }

            }

            m_requestedBlocks = new bool[NumberOfPieces][];
            for (var i = 0; i < NumberOfPieces; i++)
            {
                m_requestedBlocks[i] = new bool[ComputeNumberOfBlocks(i)];
                if (File.Exists(SaveDirectory + "\\" + Name))
                {
                    if (VerifyPiece(i))
                    {
                        for (var j = 0; j < ComputeNumberOfBlocks(i); j++)
                        {
                            m_requestedBlocks[i][j] = true;
                        }

                    }
                }

            }

            if (!m_verifiedPieces.OfType<bool>().Contains(false))
            {
                Complete = true;
                m_downloaded = Length;
            }

            for(var i = 0; i < NumberOfPieces; i++)
            {
                if (m_verifiedPieces[i])
                {
                    m_downloaded += ComputePieceLength(i);
                }
            }
            CurrentProgress = (float)m_downloaded / Length;
            Started = true;
        }
        #endregion

        #region Private Methods

        /// <summary>
        /// Computes the number of blocks in piece.
        /// </summary>
        /// <param name="a_pieceIndex">The piece index.</param>
        /// <returns>Returns the number of blocks for the given piece index.</returns>
        /// <remarks>
        /// ComputeNumberOfBlocks()
        /// 
        /// SYNOPSIS
        /// 
        ///     CommputeNumberOfBlocks(int a_pieceIndex);
        ///     
        /// DESCRIPTION
        /// 
        ///     This function will compute the number of blocks for the given 
        ///     piece index. It computes the piece length of the specific piece
        ///     and divides it by the length of a block. It calls the Math.Ceiling
        ///     function on the result and returns the result of that converted
        ///     to an integer.
        /// </remarks>
        private int ComputeNumberOfBlocks(int a_pieceIndex)
        {
            return Convert.ToInt32(Math.Ceiling(ComputePieceLength(a_pieceIndex) / (double)BlockLength));
        }

        /// <summary>
        /// Computes the block length for given piece and block index.
        /// </summary>
        /// <param name="a_pieceIndex">The piece index.</param>
        /// <param name="a_block">The block index.</param>
        /// <returns>Returns the length of the block for given parameters.</returns>
        /// <remarks>
        /// ComputeBlockLength()
        /// 
        /// SYNOPSIS
        /// 
        ///     ComputeBlockLength(int a_pieceIndex, int a_block);
        ///     
        /// DESCRIPTION
        /// 
        ///     This function will compute the block length for the given piece
        ///     and block index. If the if a_block is the last block in the piece
        ///     then we need to check if there is a remainder when we divide the
        ///     piece length by the block length. If there is a remainder, then
        ///     that is returned and if not then the BlockLength is returned.
        /// </remarks>
        private int ComputeBlockLength(int a_pieceIndex, int a_block)
        {
            if (a_block == ComputeNumberOfBlocks(a_pieceIndex) - 1)
            {
                var remainder = (ComputePieceLength(a_pieceIndex) % BlockLength);
                if (remainder != 0)
                {
                    return remainder;
                }
            }

            return BlockLength;
        }

        /// <summary>
        /// Computes the length of the info dictionary.
        /// </summary>
        /// <returns>Returns the length of the info dictionary.</returns>
        /// <remarks>
        /// ComputeInfoDictionaryLength()
        /// 
        /// SYNOPSIS
        /// 
        ///     ComputeInfoDictionaryLength();
        ///     
        /// DESCRIPTION
        /// 
        ///     This function will compute the length of the info dictionary. If
        ///     there is a single file, then it will compute starting with a base
        ///     length. It then adds on the length of the the file and the piece
        ///     hashes. If it has multiple files, then it starts with a base length
        ///     and adds the length of each file including the path and the piece
        ///     hashes.
        /// </remarks>
        private int ComputeInfoDictionaryLength()
        {
            int totalLength = 0;

            // Both single files and multi-file torrents need these calculated.
            totalLength += (PieceLength.ToString().Length);
            totalLength += m_pieces.Length.ToString().Length + m_pieces.Length;

            if (m_singleFile)
            {
                totalLength += 45;

                foreach (FileWrapper file in Files)
                {
                    totalLength += Length.ToString().Length;
                    totalLength += (Name.Length + Name).Length;
                }
            }
            else
            {
                totalLength += 11;

                foreach (FileWrapper file in Files)
                {
                    // for de and le in each file 
                    totalLength += 4;

                    // 6:lengthi****e
                    totalLength += file.Length.ToString().Length + 10;

                    // 4:path
                    totalLength += 6;

                    // split each folder
                    var folderCount = file.Path.Split('\\');
                    for (var i = 1; i < folderCount.Length; i++)
                    {
                        // for each folder
                        totalLength += folderCount[i].Length;
                        totalLength += folderCount[i].Length.ToString().Length + 1;
                    }
                }

                totalLength += Name.Length + Name.Length.ToString().Length + 7;
            }

            return totalLength;
        }

        /// <summary>
        /// Computes the sha1 hash of the info dictionary.
        /// </summary>
        /// <remarks>
        /// ComputeInfoHash()
        /// 
        /// SYNOPSIS
        /// 
        ///     ComputeInfoHash();
        ///     
        /// DESCRIPTION
        /// 
        ///     This function will compute the sha1 hash of the info dictionary.
        ///     It first must find the starting index of the info dictionary and
        ///     the length of it. Then, the info dictionary is copied to a byte
        ///     array and the info hash is computed. Once the info hash is generated
        ///     a url encoded hash is created.
        /// </remarks>
        private void ComputeInfoHash()
        {
            // Find the starting index of the info dictionary.
            var startIndex = FindInfoDictionaryStartIndex();
            startIndex += 6;

            // Find the length of the info dictionary.
            var dictionaryLength = ComputeInfoDictionaryLength();

            // Copy info dictionary into infoDictionary.
            byte[] infoDictionary = Utility.SubArray(m_rawData, startIndex, dictionaryLength);

            // Compute the sha1 hash of the info dictionary.
            using (SHA1Managed sha1 = new SHA1Managed())
            {
                ByteInfoHash = sha1.ComputeHash(infoDictionary);
            }
        }

        /// <summary>
        /// Computes the piece length for given piece index.
        /// </summary>
        /// <param name="a_pieceIndex">The piece index.</param>
        /// <returns>Returns the length of the piece.</returns>
        /// <remarks>
        /// ComputePieceLength()
        /// 
        /// SYNPOSIS
        /// 
        ///     ComputePieceLength(int a_pieceIndex)
        ///     
        /// DESCRIPTION
        /// 
        ///     This function will compute the piece length for the given piece
        ///     index. If the piece is the last piece, then it checks if there is
        ///     a remainder when the Length of the torrent is divided by the 
        ///     PieceLength. If there is a remainder then that is returned and if 
        ///     not PieceLength is returned.
        /// </remarks>
        public int ComputePieceLength(int a_pieceIndex)
        {
            if (a_pieceIndex == NumberOfPieces - 1)
            {
                var remainder = (int)(Length % PieceLength);
                if (remainder != 0)
                {
                    return remainder;
                }
            }

            return (int)PieceLength;
        }

        private void ComputeRarestPieces()
        {
            m_downloadOrder.Clear();
            foreach(var item in m_pieceOccurances.OrderBy(i => i.Value))
            {
                m_downloadOrder.Add(item.Key, item.Value);
            }
        }
        /// <summary>
        /// Decodes the torrent.
        /// </summary>
        /// <remarks>
        /// DecodeTorrent()
        /// 
        /// SYNOPSIS
        /// 
        ///     DecodeTorrent();
        ///     
        /// DESCRIPTION
        /// 
        ///     This function will decode the torrent. It will first decode the
        ///     bencoded raw data. It will then call the SetMetaData function
        ///     setting all class fields and properties. The info hash is then
        ///     computed.
        /// </remarks>
        private void DecodeTorrent()
        {
            var decodedData = Bencode.BDecode(m_rawData);

            SetMetaData(decodedData.ElementAt(0).Value);

            ComputeInfoHash();
               
            for(var i = 0; i < NumberOfPieces; i++)
            {
                m_pieceOccurances.Add(i, 0);
            }
        }

        /// <summary>
        /// Finds the starting index of the info dictionary in the raw byte data.
        /// </summary>
        /// <returns>
        /// Returns the start index of the info dictionary. If it cannot find 
        /// the info dictionary then -1 is returned.
        /// </returns>
        /// <remarks>
        /// FindInfoDictionaryStartIndex()
        /// 
        /// SYNOPSIS
        /// 
        ///     FindInfoDictionaryStartIndex();
        ///     
        /// DESCRIPTION
        /// 
        ///     The function will find the start index of the info dictionary.
        ///     It will loop through m_rawData looking for the byte sequence equal
        ///     to "4:infod".
        /// </remarks>
        private int FindInfoDictionaryStartIndex()
        {
            // Bytes to find "4:infod".
            byte[] toMatch = { 52, 58, 105, 110, 102, 111, 100 };

            
            var length = toMatch.Length;
            var limit = m_rawData.Length - length;

            // Go through raw data and find the match.
            for (var i = 0; i < limit; i++)
            {
                var j = 0;
                for (; j < length; j++)
                {
                    if (toMatch[j] != m_rawData[i + j])
                    {
                        break;
                    }
                }
                if (j == length)
                {
                    return i;
                }
            }

            // Return -1 if it could not find.
            return -1;
        }

        /// <summary>
        /// Sets info dictionary fields and properties.
        /// </summary>
        /// <param name="a_dictionary">The info dictionary to parse.</param>
        /// <remarks>
        /// SetInfoDictionaryData()
        /// 
        /// SYNOPSIS
        /// 
        ///     SetInfoDictionaryData(Dictionary<string, BDecodedObject> a_dictionary);
        ///     
        /// DESCRIPTION
        ///     
        ///     This function will set the fields and properties of the info 
        ///     dictionary. This includes the piece length, piece hashes, private,
        ///     and file data.
        /// </remarks>
        private void SetInfoDictionaryData(Dictionary<string, BDecodedObject> a_dictionary)
        {
            // Used to turn bytes into utf8 encoded strings.
            var utf8 = System.Text.Encoding.UTF8;

            // For both multi-file and single-file torrents.
            PieceLength = a_dictionary["piece length"].Value;
            m_pieces = a_dictionary["pieces"].Value;

            // Private is optional.
            if (a_dictionary.ContainsKey("private"))
            {
                m_privateTracker = (long)a_dictionary["private"].Value;
            }

            // Multi-file torrent.
            if (a_dictionary.ContainsKey("files"))
            {
                m_singleFile = false;
                // The name of the torrent file.
                Name = utf8.GetString(a_dictionary["name"].Value);

                long offset = 0;
                // For each file.
                foreach (BDecodedDictionary dictionary in a_dictionary["files"].Value)
                {
                    FileWrapper file = new FileWrapper();
                    file.StartOffset = offset;
                    file.Length = dictionary.Value["length"].Value;

                    offset += file.Length;
                    file.EndOffset = offset;

                    // The initial path folder is the name of the torrent.
                    file.Path = Name;

                    // Md5sum is optional.
                    if (a_dictionary.ContainsKey("md5sum"))
                    {
                        file.MD5Sum = a_dictionary["md5sum"].Value;
                    }

                    // Path contains directories
                    foreach (BDecodedString list in dictionary.Value["path"].Value)
                    {
                        file.Path += "\\";
                        file.Path += utf8.GetString(list.Value);
                    }
                    
                    Files.Add(file);
                }
                Length = offset;

            }
            // Single-file torrent.
            else
            {
                m_singleFile = true;
                Name = utf8.GetString(a_dictionary["name"].Value);

                Length = a_dictionary["length"].Value;
                FileWrapper file = new FileWrapper();

                // The name is the same as the torrent name.
                file.Name = utf8.GetString(a_dictionary["name"].Value);
                // The length is the same as the torrent length
                file.Length = a_dictionary["length"].Value;

                // Md5sum is optional.
                if (a_dictionary.ContainsKey("md5sum"))
                {
                    file.MD5Sum = a_dictionary["md5sum"].Value;
                }
                file.EndOffset = file.Length;
                file.StartOffset = 0;
                file.Path += "\\";
                file.Path += file.Name;
                Files.Add(file);
            }
        }

        /// <summary>
        /// Sets torrent metadata fields and properteies.
        /// </summary>
        /// <param name="a_dictionary">Dictionary containing the torrent metadata.</param>
        /// <remarks>
        /// SetMetaData()
        /// 
        /// SYNOPSIS
        ///     
        ///     SetMetaData(Dictionary<string, BDecodedObject> a_dictionary);
        ///     
        /// DESCRIPTION
        /// 
        ///     This function will set the fields and properties of the torrent 
        ///     metadata. This includes the announce url's, the creation date, 
        ///     comment, created by and encoding. It will then call the function
        ///     SetInfoDictionaryData.
        /// </remarks>
        private void SetMetaData(Dictionary<string, BDecodedObject> a_dictionary)
        {
            // Used to turn bytes into utf8 encoded strings.
            var utf8 = System.Text.Encoding.UTF8;

            // Add the announce url and any other extra announce url's in the 
            // announce-list.
            m_announceList.Add(utf8.GetString(a_dictionary["announce"].Value));
            // Announce-list is optional.
            if (a_dictionary.ContainsKey("announce-list"))
            {
                foreach (BDecodedList innerList in a_dictionary["announce-list"].Value)
                {
                    foreach (BDecodedString announceInInner in innerList.Value)
                    {
                        string announce = utf8.GetString(announceInInner.Value);

                        if (!m_announceList.Contains(announce))
                        {
                            m_announceList.Add(announce);
                        }
                    }
                }
            }

            // Creation date is optional.
            if (a_dictionary.ContainsKey("creation date"))
            {
                CreationDate = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
                double secondsSince = Convert.ToDouble(a_dictionary["creation date"].Value);
                CreationDate = CreationDate.AddSeconds(secondsSince).ToLocalTime();
            }

            // Comment is optional.
            if (a_dictionary.ContainsKey("comment"))
            {
                Comment = utf8.GetString(a_dictionary["comment"].Value);
            }

            // Created by is optional.
            if (a_dictionary.ContainsKey("created by"))
            {
                CreatedBy = utf8.GetString(a_dictionary["created by"].Value);
            }

            // Encoding is optional.
            if (a_dictionary.ContainsKey("encoding"))
            {
                Encoding = utf8.GetString(a_dictionary["encoding"].Value);
            }

            // Parses info dictionary data.
            SetInfoDictionaryData(a_dictionary["info"].Value); 
        }

        /// <summary>
        /// Sets peer event handlers and connects.
        /// </summary>
        /// <param name="a_peer">The peer to set event handlers.</param>
        /// <remarks>
        /// SetPeerEventHandlers()
        /// 
        /// SYNPOPSIS
        /// 
        ///     SetPeerEventHandlers(Peer a_peer);
        ///     
        /// DESCRIPTION
        /// 
        ///     This function will set the even handlers for peer. It will also
        ///     call the connect function in the peer. If it successfully connects
        ///     it will add the peer to the Peers concurrent dictionary.
        /// </remarks>
        private void SetPeerEventHandlers(Peer a_peer)
        {
            // Set event handlers.
            a_peer.BitfieldRecieved += HandleBitfieldRecieved;
            a_peer.Disconnected += HandlePeerDisconnected;
            a_peer.HaveRecieved += HandleHaveReceived;
            a_peer.BlockRequested += HandleBlockRequested;
            a_peer.BlockCanceled += HandleBlockCanceled;
            a_peer.BlockReceived += HandleBlockReceived;

            // Connect to peer.
            a_peer.Connect();
            // If sucessfully connected to peer then add to Peers.
            if (a_peer.Connected)
            {
                Peers.TryAdd(a_peer.IP, a_peer);
            }
        }

        private bool VerifyPiece(int a_pieceIndex)
        {
            byte[] pieceHash = Utility.SubArray(m_pieces, a_pieceIndex * 20, 20);

            byte[] piece = TorrentIO.ReadPiece(a_pieceIndex, this);
            SHA1Managed sha1 = new SHA1Managed();
            using (sha1)
            {
                if (!pieceHash.SequenceEqual(sha1.ComputeHash(piece)))
                {
                    return false;
                }
            }
       
            return true;
        }
     
        #endregion

        #endregion
    }
}