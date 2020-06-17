﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace FingerPuppet.WSQ
{
    public class WsqDecoder : IWsqDecoder
    {
        public byte[] Decode(byte[] data)
        {
            var token = new Token(data);

            token.Initialize();

            /* Read the SOI marker. */
            GetCMarkerWSQ(token, WsqHelper.SOI_WSQ);

            /* Read in supporting tables up to the SOF marker. */
            var marker = GetCMarkerWSQ(token, WsqHelper.TBLS_N_SOF);
            while (marker != WsqHelper.SOF_WSQ)
            {
                GetCTableWSQ(token, marker);
                marker = GetCMarkerWSQ(token, WsqHelper.TBLS_N_SOF);
            }

            /* Read in the Frame Header. */
            var frmHeaderWSQ = GetCFrameHeaderWSQ(token);
            var width = frmHeaderWSQ.width;
            var height = frmHeaderWSQ.height;

            var ppi = GetCPpiWSQ();

            /* Build WSQ decomposition trees. */
            BuildWSQTrees(token, width, height);

            /* Decode the Huffman encoded buffer blocks. */
            var qdata = HuffmanDecodeDataMem(token, width * height);

            /* Decode the quantize wavelet subband buffer. */
            var fdata = Unquantize(token, qdata, width, height);

            /* Done with quantized wavelet subband buffer. */

            WsqReconstruct(token, fdata, width, height);

            /* Convert floating point pixels to unsigned char pixels. */
            var cdata = ConvertImage2Byte(fdata, width, height, frmHeaderWSQ.mShift, frmHeaderWSQ.rScale);

            using var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            var pix = 0;
            for (var i = 0; i < height; i++)
            for (var j = 0; j < width; j++)
            {
                bmp.SetPixel(j, i, Color.FromArgb(cdata[pix], cdata[pix], cdata[pix]));
                pix++;
            }

            bmp.RotateFlip(RotateFlipType.Rotate180FlipX);

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Bmp);

            return ms.ToArray();
        }

        private static int IntSign(int power)
        {
            /* "sign" power */
            int cnt; /* counter */
            var num = -1; /* sign return value */

            if (power == 0)
                return 1;

            for (cnt = 1; cnt < power; cnt++)
                num *= -1;

            return num;
        }

        private static int GetCMarkerWSQ(Token token, int type)
        {
            if (token.pointer >= token.buffer.Length)
                throw new SystemException("Error, Invalid pointer : " + token.pointer);

            var marker = token.ReadShort();

            switch (type)
            {
                case WsqHelper.SOI_WSQ:
                    if (marker != WsqHelper.SOI_WSQ)
                        throw new SystemException("ERROR : getCMarkerWSQ : No SOI marker : " + marker);

                    return marker;
                case WsqHelper.TBLS_N_SOF:
                    if (marker != WsqHelper.DTT_WSQ
                        && marker != WsqHelper.DQT_WSQ
                        && marker != WsqHelper.DHT_WSQ
                        && marker != WsqHelper.SOF_WSQ
                        && marker != WsqHelper.COM_WSQ)
                        throw new SystemException("ERROR : getc_marker_wsq : No SOF, Table, or comment markers : " +
                                                  marker);

                    return marker;
                case WsqHelper.TBLS_N_SOB:
                    if (marker != WsqHelper.DTT_WSQ
                        && marker != WsqHelper.DQT_WSQ
                        && marker != WsqHelper.DHT_WSQ
                        && marker != WsqHelper.SOB_WSQ
                        && marker != WsqHelper.COM_WSQ)
                        throw new SystemException("ERROR : getc_marker_wsq : No SOB, Table, or comment markers : " +
                                                  marker);

                    return marker;
                case WsqHelper.ANY_WSQ:
                    if ((marker & 0xff00) != 0xff00)
                        throw new SystemException("ERROR : getc_marker_wsq : no marker found : " + marker);

                    /* Added by MDG on 03-07-05 */
                    if (marker < WsqHelper.SOI_WSQ || marker > WsqHelper.COM_WSQ)
                        throw new SystemException("ERROR : getc_marker_wsq : not a valid marker : " + marker);

                    return marker;
                default:
                    throw new SystemException("ERROR : getc_marker_wsq : Invalid marker : " + marker);
            }
        }

        private static void GetCTableWSQ(Token token, int marker)
        {
            switch (marker)
            {
                case WsqHelper.DTT_WSQ:
                    GetCTransformTable(token);
                    return;
                case WsqHelper.DQT_WSQ:
                    GetCQuantizationTable(token);
                    return;
                case WsqHelper.DHT_WSQ:
                    GetCHuffmanTableWSQ(token);
                    return;
                case WsqHelper.COM_WSQ:
                    //shams: i don't use return value
                    GetCComment(token);
                    return;
                default:
                    throw new SystemException("ERROR: getCTableWSQ : Invalid table defined : " + marker);
            }
        }

        private static string GetCComment(Token token)
        {
            var size = token.ReadShort() - 2;
            var t = token.ReadBytes(size);

            return Encoding.ASCII.GetString(t.ToArray(), 0, t.Length);
        }

        private static void GetCTransformTable(Token token)
        {
            // read header Size;
            token.ReadShort();

            token.tableDTT.hisz = token.ReadByte();
            token.tableDTT.losz = token.ReadByte();

            token.tableDTT.hifilt = new float[token.tableDTT.hisz];
            token.tableDTT.lofilt = new float[token.tableDTT.losz];

            var aSize = token.tableDTT.hisz % 2 != 0 ? (token.tableDTT.hisz + 1) / 2 : token.tableDTT.hisz / 2;

            var aLofilt = new float[aSize];

            aSize--;
            for (var cnt = 0; cnt <= aSize; cnt++)
            {
                var sign = token.ReadByte();
                var scale = token.ReadByte();
                var shrtDat = token.ReadInt();
                aLofilt[cnt] = shrtDat;

                while (scale > 0)
                {
                    aLofilt[cnt] /= 10.0F;
                    scale--;
                }

                if (sign != 0)
                    aLofilt[cnt] *= -1.0F;

                if (token.tableDTT.hisz % 2 != 0)
                {
                    token.tableDTT.hifilt[cnt + aSize] = IntSign(cnt) * aLofilt[cnt];
                    if (cnt > 0)
                        token.tableDTT.hifilt[aSize - cnt] = token.tableDTT.hifilt[cnt + aSize];
                }
                else
                {
                    token.tableDTT.hifilt[cnt + aSize + 1] = IntSign(cnt) * aLofilt[cnt];
                    token.tableDTT.hifilt[aSize - cnt] = -1 * token.tableDTT.hifilt[cnt + aSize + 1];
                }
            }

            aSize = token.tableDTT.losz % 2 != 0 ? (token.tableDTT.losz + 1) / 2 : token.tableDTT.losz / 2;

            var aHifilt = new float[aSize];

            aSize--;
            for (var cnt = 0; cnt <= aSize; cnt++)
            {
                var sign = token.ReadByte();
                var scale = token.ReadByte();
                var shrtDat = token.ReadInt();

                aHifilt[cnt] = shrtDat;

                while (scale > 0)
                {
                    aHifilt[cnt] /= 10.0F;
                    scale--;
                }

                if (sign != 0)
                    aHifilt[cnt] *= -1.0F;

                if (token.tableDTT.losz % 2 != 0)
                {
                    token.tableDTT.lofilt[cnt + aSize] = IntSign(cnt) * aHifilt[cnt];
                    if (cnt > 0)
                        token.tableDTT.lofilt[aSize - cnt] = token.tableDTT.lofilt[cnt + aSize];
                }
                else
                {
                    token.tableDTT.lofilt[cnt + aSize + 1] = IntSign(cnt + 1) * aHifilt[cnt];
                    token.tableDTT.lofilt[aSize - cnt] = token.tableDTT.lofilt[cnt + aSize + 1];
                }
            }

            token.tableDTT.lodef = 1;
            token.tableDTT.hidef = 1;
        }

        public static void GetCQuantizationTable(Token token)
        {
            token.ReadShort(); /* header size */
            var scale = token.ReadByte(); /* scaling parameter */
            var shrtDat = token.ReadShort(); /* counter and temp short buffer */

            token.tableDQT.binCenter = shrtDat;
            while (scale > 0)
            {
                token.tableDQT.binCenter /= 10.0F;
                scale--;
            }

            for (var cnt = 0; cnt < Table_DQT.MAX_SUBBANDS; cnt++)
            {
                scale = token.ReadByte();
                shrtDat = token.ReadShort();
                token.tableDQT.qBin[cnt] = shrtDat;
                while (scale > 0)
                {
                    token.tableDQT.qBin[cnt] /= 10.0F;
                    scale--;
                }

                scale = token.ReadByte();
                shrtDat = token.ReadShort();
                token.tableDQT.zBin[cnt] = shrtDat;
                while (scale > 0)
                {
                    token.tableDQT.zBin[cnt] /= 10.0F;
                    scale--;
                }
            }

            token.tableDQT.dqtDef = 1;
        }

        public static void GetCHuffmanTableWSQ(Token token)
        {
            /* First time, read table len. */
            var firstHuffmanTable = GetCHuffmanTable(token, WsqHelper.MAX_HUFFCOUNTS_WSQ, 0, true);

            /* Store table into global structure list. */
            var tableId = firstHuffmanTable.tableId;
            token.tableDHT[tableId].huffbits = (int[]) firstHuffmanTable.huffbits.Clone();
            token.tableDHT[tableId].huffvalues = (int[]) firstHuffmanTable.huffvalues.Clone();
            token.tableDHT[tableId].tabdef = 1;

            var bytesLeft = firstHuffmanTable.bytesLeft;
            while (bytesLeft != 0)
            {
                /* Read next table without rading table len. */
                var huffmantable = GetCHuffmanTable(token, WsqHelper.MAX_HUFFCOUNTS_WSQ, bytesLeft, false);

                /* If table is already defined ... */
                tableId = huffmantable.tableId;
                if (token.tableDHT[tableId].tabdef != 0)
                    throw new SystemException("ERROR : getCHuffmanTableWSQ : huffman table already defined.");

                /* Store table into global structure list. */
                token.tableDHT[tableId].huffbits = (int[]) huffmantable.huffbits.Clone();
                token.tableDHT[tableId].huffvalues = (int[]) huffmantable.huffvalues.Clone();
                token.tableDHT[tableId].tabdef = 1;
                bytesLeft = huffmantable.bytesLeft;
            }
        }

        private static HuffmanTable GetCHuffmanTable(Token token, int maxHuffcounts, int bytesLeft, bool readTableLen)
        {
            var huffmanTable = new HuffmanTable();

            /* table_len */
            if (readTableLen)
            {
                huffmanTable.tableLen = token.ReadShort();
                huffmanTable.bytesLeft = huffmanTable.tableLen - 2;
                bytesLeft = huffmanTable.bytesLeft;
            }
            else
            {
                huffmanTable.bytesLeft = bytesLeft;
            }

            /* If no bytes left ... */
            if (bytesLeft <= 0)
                throw new SystemException("ERROR : getCHuffmanTable : no huffman table bytes remaining");

            /* Table ID */
            huffmanTable.tableId = token.ReadByte();
            huffmanTable.bytesLeft--;

            huffmanTable.huffbits = new int[WsqHelper.MAX_HUFFBITS];
            var numHufvals = 0;
            /* L1 ... L16 */
            for (var i = 0; i < WsqHelper.MAX_HUFFBITS; i++)
            {
                huffmanTable.huffbits[i] = token.ReadByte();
                numHufvals += huffmanTable.huffbits[i];
            }

            huffmanTable.bytesLeft -= WsqHelper.MAX_HUFFBITS;

            if (numHufvals > maxHuffcounts + 1)
                throw new SystemException("ERROR : getCHuffmanTable : numHufvals is larger than MAX_HUFFCOUNTS");

            /* Could allocate only the amount needed ... then we wouldn't */
            /* need to pass MAX_HUFFCOUNTS. */
            huffmanTable.huffvalues = new int[maxHuffcounts + 1];

            /* V1,1 ... V16,16 */
            for (var i = 0; i < numHufvals; i++)
                huffmanTable.huffvalues[i] = token.ReadByte();

            huffmanTable.bytesLeft -= numHufvals;

            return huffmanTable;
        }

        private static HeaderFrm GetCFrameHeaderWSQ(Token token)
        {
            var headerFrm = new HeaderFrm();

            //noinspection UnusedDeclaration
            token.ReadShort();

            headerFrm.black = token.ReadByte();
            headerFrm.white = token.ReadByte();
            headerFrm.height = token.ReadShort();
            headerFrm.width = token.ReadShort();
            var scale = token.ReadByte(); /* exponent scaling parameter */
            var shrtDat = token.ReadShort(); /* buffer pointer */
            headerFrm.mShift = shrtDat;
            while (scale > 0)
            {
                headerFrm.mShift /= 10.0F;
                scale--;
            }

            scale = token.ReadByte();
            shrtDat = token.ReadShort();
            headerFrm.rScale = shrtDat;
            while (scale > 0)
            {
                headerFrm.rScale /= 10.0F;
                scale--;
            }

            headerFrm.wsqEncoder = token.ReadByte();
            headerFrm.software = token.ReadShort();

            return headerFrm;
        }

        private static int GetCPpiWSQ()
        {
            return -1;
        }

        private static void BuildWSQTrees(Token token, int width, int height)
        {
            /* Build a W-TREE structure for the image. */
            BuildWTree(token, WsqHelper.W_TREELEN, width, height);
            /* Build a Q-TREE structure for the image. */
            BuildQTree(token, WsqHelper.Q_TREELEN);
        }

        private static void BuildWTree(Token token, int wtreelen, int width, int height)
        {
            int lenx, lenx2, leny, leny2; /* starting lengths of sections of
                                                  the image being split into subbands */
            token.wtree = new WavletTree[wtreelen];
            for (var i = 0; i < wtreelen; i++)
                token.wtree[i] = new WavletTree
                {
                    invrw = 0,
                    invcl = 0
                };

            token.wtree[2].invrw = 1;
            token.wtree[4].invrw = 1;
            token.wtree[7].invrw = 1;
            token.wtree[9].invrw = 1;
            token.wtree[11].invrw = 1;
            token.wtree[13].invrw = 1;
            token.wtree[16].invrw = 1;
            token.wtree[18].invrw = 1;
            token.wtree[3].invcl = 1;
            token.wtree[5].invcl = 1;
            token.wtree[8].invcl = 1;
            token.wtree[9].invcl = 1;
            token.wtree[12].invcl = 1;
            token.wtree[13].invcl = 1;
            token.wtree[17].invcl = 1;
            token.wtree[18].invcl = 1;

            Wtree4(token, 0, 1, width, height, 0, 0, 1);

            if (token.wtree[1].lenx % 2 == 0)
            {
                lenx = token.wtree[1].lenx / 2;
                lenx2 = lenx;
            }
            else
            {
                lenx = (token.wtree[1].lenx + 1) / 2;
                lenx2 = lenx - 1;
            }

            if (token.wtree[1].leny % 2 == 0)
            {
                leny = token.wtree[1].leny / 2;
                leny2 = leny;
            }
            else
            {
                leny = (token.wtree[1].leny + 1) / 2;
                leny2 = leny - 1;
            }

            Wtree4(token, 4, 6, lenx2, leny, lenx, 0, 0);
            Wtree4(token, 5, 10, lenx, leny2, 0, leny, 0);
            Wtree4(token, 14, 15, lenx, leny, 0, 0, 0);

            token.wtree[19].x = 0;
            token.wtree[19].y = 0;
            if (token.wtree[15].lenx % 2 == 0)
                token.wtree[19].lenx = token.wtree[15].lenx / 2;
            else
                token.wtree[19].lenx = (token.wtree[15].lenx + 1) / 2;

            if (token.wtree[15].leny % 2 == 0)
                token.wtree[19].leny = token.wtree[15].leny / 2;
            else
                token.wtree[19].leny = (token.wtree[15].leny + 1) / 2;
        }

        private static void Wtree4(Token token, int start1, int start2, int lenx, int leny, int x, int y, int stop1)
        {
            var p1 = start1;
            var p2 = start2;

            var evenx = lenx % 2;
            var eveny = leny % 2;

            token.wtree[p1].x = x;
            token.wtree[p1].y = y;
            token.wtree[p1].lenx = lenx;
            token.wtree[p1].leny = leny;

            token.wtree[p2].x = x;
            token.wtree[p2 + 2].x = x;
            token.wtree[p2].y = y;
            token.wtree[p2 + 1].y = y;

            if (evenx == 0)
            {
                token.wtree[p2].lenx = lenx / 2;
                token.wtree[p2 + 1].lenx = token.wtree[p2].lenx;
            }
            else
            {
                if (p1 == 4)
                {
                    token.wtree[p2].lenx = (lenx - 1) / 2;
                    token.wtree[p2 + 1].lenx = token.wtree[p2].lenx + 1;
                }
                else
                {
                    token.wtree[p2].lenx = (lenx + 1) / 2;
                    token.wtree[p2 + 1].lenx = token.wtree[p2].lenx - 1;
                }
            }

            token.wtree[p2 + 1].x = token.wtree[p2].lenx + x;
            if (stop1 == 0)
            {
                token.wtree[p2 + 3].lenx = token.wtree[p2 + 1].lenx;
                token.wtree[p2 + 3].x = token.wtree[p2 + 1].x;
            }

            token.wtree[p2 + 2].lenx = token.wtree[p2].lenx;

            if (eveny == 0)
            {
                token.wtree[p2].leny = leny / 2;
                token.wtree[p2 + 2].leny = token.wtree[p2].leny;
            }
            else
            {
                if (p1 == 5)
                {
                    token.wtree[p2].leny = (leny - 1) / 2;
                    token.wtree[p2 + 2].leny = token.wtree[p2].leny + 1;
                }
                else
                {
                    token.wtree[p2].leny = (leny + 1) / 2;
                    token.wtree[p2 + 2].leny = token.wtree[p2].leny - 1;
                }
            }

            token.wtree[p2 + 2].y = token.wtree[p2].leny + y;
            if (stop1 == 0)
            {
                token.wtree[p2 + 3].leny = token.wtree[p2 + 2].leny;
                token.wtree[p2 + 3].y = token.wtree[p2 + 2].y;
            }

            token.wtree[p2 + 1].leny = token.wtree[p2].leny;
        }

        private static void BuildQTree(Token token, int qtreelen)
        {
            token.qtree = new QuantTree[qtreelen];
            for (var i = 0; i < token.qtree.Length; i++)
                token.qtree[i] = new QuantTree();

            Qtree16(token, 3, token.wtree[14].lenx, token.wtree[14].leny, token.wtree[14].x, token.wtree[14].y, 0, 0);
            Qtree16(token, 19, token.wtree[4].lenx, token.wtree[4].leny, token.wtree[4].x, token.wtree[4].y, 0, 1);
            Qtree16(token, 48, token.wtree[0].lenx, token.wtree[0].leny, token.wtree[0].x, token.wtree[0].y, 0, 0);
            Qtree16(token, 35, token.wtree[5].lenx, token.wtree[5].leny, token.wtree[5].x, token.wtree[5].y, 1, 0);
            Qtree4(token, 0, token.wtree[19].lenx, token.wtree[19].leny, token.wtree[19].x, token.wtree[19].y);
        }

        private static void Qtree16(Token token, int start, int lenx, int leny, int x, int y, int rw, int cl)
        {
            int tempx, temp2x; /* temporary x values */
            int tempy, temp2y; /* temporary y values */

            var p = start;
            var evenx = lenx % 2;
            var eveny = leny % 2;

            if (evenx == 0)
            {
                tempx = lenx / 2;
                temp2x = tempx;
            }
            else
            {
                if (cl != 0)
                {
                    temp2x = (lenx + 1) / 2;
                    tempx = temp2x - 1;
                }
                else
                {
                    tempx = (lenx + 1) / 2;
                    temp2x = tempx - 1;
                }
            }

            if (eveny == 0)
            {
                tempy = leny / 2;
                temp2y = tempy;
            }
            else
            {
                if (rw != 0)
                {
                    temp2y = (leny + 1) / 2;
                    tempy = temp2y - 1;
                }
                else
                {
                    tempy = (leny + 1) / 2;
                    temp2y = tempy - 1;
                }
            }

            evenx = tempx % 2;
            eveny = tempy % 2;

            token.qtree[p].x = x;
            token.qtree[p + 2].x = x;
            token.qtree[p].y = y;
            token.qtree[p + 1].y = y;
            if (evenx == 0)
            {
                token.qtree[p].lenx = tempx / 2;
                token.qtree[p + 1].lenx = token.qtree[p].lenx;
                token.qtree[p + 2].lenx = token.qtree[p].lenx;
                token.qtree[p + 3].lenx = token.qtree[p].lenx;
            }
            else
            {
                token.qtree[p].lenx = (tempx + 1) / 2;
                token.qtree[p + 1].lenx = token.qtree[p].lenx - 1;
                token.qtree[p + 2].lenx = token.qtree[p].lenx;
                token.qtree[p + 3].lenx = token.qtree[p + 1].lenx;
            }

            token.qtree[p + 1].x = x + token.qtree[p].lenx;
            token.qtree[p + 3].x = token.qtree[p + 1].x;
            if (eveny == 0)
            {
                token.qtree[p].leny = tempy / 2;
                token.qtree[p + 1].leny = token.qtree[p].leny;
                token.qtree[p + 2].leny = token.qtree[p].leny;
                token.qtree[p + 3].leny = token.qtree[p].leny;
            }
            else
            {
                token.qtree[p].leny = (tempy + 1) / 2;
                token.qtree[p + 1].leny = token.qtree[p].leny;
                token.qtree[p + 2].leny = token.qtree[p].leny - 1;
                token.qtree[p + 3].leny = token.qtree[p + 2].leny;
            }

            token.qtree[p + 2].y = y + token.qtree[p].leny;
            token.qtree[p + 3].y = token.qtree[p + 2].y;

            evenx = temp2x % 2;

            token.qtree[p + 4].x = x + tempx;
            token.qtree[p + 6].x = token.qtree[p + 4].x;
            token.qtree[p + 4].y = y;
            token.qtree[p + 5].y = y;
            token.qtree[p + 6].y = token.qtree[p + 2].y;
            token.qtree[p + 7].y = token.qtree[p + 2].y;
            token.qtree[p + 4].leny = token.qtree[p].leny;
            token.qtree[p + 5].leny = token.qtree[p].leny;
            token.qtree[p + 6].leny = token.qtree[p + 2].leny;
            token.qtree[p + 7].leny = token.qtree[p + 2].leny;
            if (evenx == 0)
            {
                token.qtree[p + 4].lenx = temp2x / 2;
                token.qtree[p + 5].lenx = token.qtree[p + 4].lenx;
                token.qtree[p + 6].lenx = token.qtree[p + 4].lenx;
                token.qtree[p + 7].lenx = token.qtree[p + 4].lenx;
            }
            else
            {
                token.qtree[p + 5].lenx = (temp2x + 1) / 2;
                token.qtree[p + 4].lenx = token.qtree[p + 5].lenx - 1;
                token.qtree[p + 6].lenx = token.qtree[p + 4].lenx;
                token.qtree[p + 7].lenx = token.qtree[p + 5].lenx;
            }

            token.qtree[p + 5].x = token.qtree[p + 4].x + token.qtree[p + 4].lenx;
            token.qtree[p + 7].x = token.qtree[p + 5].x;

            eveny = temp2y % 2;

            token.qtree[p + 8].x = x;
            token.qtree[p + 9].x = token.qtree[p + 1].x;
            token.qtree[p + 10].x = x;
            token.qtree[p + 11].x = token.qtree[p + 1].x;
            token.qtree[p + 8].y = y + tempy;
            token.qtree[p + 9].y = token.qtree[p + 8].y;
            token.qtree[p + 8].lenx = token.qtree[p].lenx;
            token.qtree[p + 9].lenx = token.qtree[p + 1].lenx;
            token.qtree[p + 10].lenx = token.qtree[p].lenx;
            token.qtree[p + 11].lenx = token.qtree[p + 1].lenx;
            if (eveny == 0)
            {
                token.qtree[p + 8].leny = temp2y / 2;
                token.qtree[p + 9].leny = token.qtree[p + 8].leny;
                token.qtree[p + 10].leny = token.qtree[p + 8].leny;
                token.qtree[p + 11].leny = token.qtree[p + 8].leny;
            }
            else
            {
                token.qtree[p + 10].leny = (temp2y + 1) / 2;
                token.qtree[p + 11].leny = token.qtree[p + 10].leny;
                token.qtree[p + 8].leny = token.qtree[p + 10].leny - 1;
                token.qtree[p + 9].leny = token.qtree[p + 8].leny;
            }

            token.qtree[p + 10].y = token.qtree[p + 8].y + token.qtree[p + 8].leny;
            token.qtree[p + 11].y = token.qtree[p + 10].y;

            token.qtree[p + 12].x = token.qtree[p + 4].x;
            token.qtree[p + 13].x = token.qtree[p + 5].x;
            token.qtree[p + 14].x = token.qtree[p + 4].x;
            token.qtree[p + 15].x = token.qtree[p + 5].x;
            token.qtree[p + 12].y = token.qtree[p + 8].y;
            token.qtree[p + 13].y = token.qtree[p + 8].y;
            token.qtree[p + 14].y = token.qtree[p + 10].y;
            token.qtree[p + 15].y = token.qtree[p + 10].y;
            token.qtree[p + 12].lenx = token.qtree[p + 4].lenx;
            token.qtree[p + 13].lenx = token.qtree[p + 5].lenx;
            token.qtree[p + 14].lenx = token.qtree[p + 4].lenx;
            token.qtree[p + 15].lenx = token.qtree[p + 5].lenx;
            token.qtree[p + 12].leny = token.qtree[p + 8].leny;
            token.qtree[p + 13].leny = token.qtree[p + 8].leny;
            token.qtree[p + 14].leny = token.qtree[p + 10].leny;
            token.qtree[p + 15].leny = token.qtree[p + 10].leny;
        }

        private static void Qtree4(Token token, int start, int lenx, int leny, int x, int y)
        {
            var p = start;
            var evenx = lenx % 2;
            var eveny = leny % 2;

            token.qtree[p].x = x;
            token.qtree[p + 2].x = x;
            token.qtree[p].y = y;
            token.qtree[p + 1].y = y;
            if (evenx == 0)
            {
                token.qtree[p].lenx = lenx / 2;
                token.qtree[p + 1].lenx = token.qtree[p].lenx;
                token.qtree[p + 2].lenx = token.qtree[p].lenx;
                token.qtree[p + 3].lenx = token.qtree[p].lenx;
            }
            else
            {
                token.qtree[p].lenx = (lenx + 1) / 2;
                token.qtree[p + 1].lenx = token.qtree[p].lenx - 1;
                token.qtree[p + 2].lenx = token.qtree[p].lenx;
                token.qtree[p + 3].lenx = token.qtree[p + 1].lenx;
            }

            token.qtree[p + 1].x = x + token.qtree[p].lenx;
            token.qtree[p + 3].x = token.qtree[p + 1].x;
            if (eveny == 0)
            {
                token.qtree[p].leny = leny / 2;
                token.qtree[p + 1].leny = token.qtree[p].leny;
                token.qtree[p + 2].leny = token.qtree[p].leny;
                token.qtree[p + 3].leny = token.qtree[p].leny;
            }
            else
            {
                token.qtree[p].leny = (leny + 1) / 2;
                token.qtree[p + 1].leny = token.qtree[p].leny;
                token.qtree[p + 2].leny = token.qtree[p].leny - 1;
                token.qtree[p + 3].leny = token.qtree[p + 2].leny;
            }

            token.qtree[p + 2].y = y + token.qtree[p].leny;
            token.qtree[p + 3].y = token.qtree[p + 2].y;
        }

        private static int[] HuffmanDecodeDataMem(Token token, int size)
        {
            var qdata = new int[size];

            var maxcode = new int[WsqHelper.MAX_HUFFBITS + 1];
            var mincode = new int[WsqHelper.MAX_HUFFBITS + 1];
            var valptr = new int[WsqHelper.MAX_HUFFBITS + 1];

            var marker = new IntRef(GetCMarkerWSQ(token, WsqHelper.TBLS_N_SOB));

            var bitCount = new IntRef(0); /* bit count for getc_nextbits_wsq routine */
            var nextByte = new IntRef(0); /*next byte of buffer*/
            var hufftableId = 0; /* huffman table number */
            var ip = 0;

            while (marker.value != WsqHelper.EOI_WSQ)
            {
                if (marker.value != 0)
                {
                    while (marker.value != WsqHelper.SOB_WSQ)
                    {
                        GetCTableWSQ(token, marker.value);
                        marker.value = GetCMarkerWSQ(token, WsqHelper.TBLS_N_SOB);
                    }

                    hufftableId = GetCBlockHeader(token); /* huffman table number */

                    if (token.tableDHT[hufftableId].tabdef != 1)
                        throw new SystemException("ERROR : huffmanDecodeDataMem : huffman table undefined.");

                    /* the next two routines reconstruct the huffman tables */
                    var hufftable = BuildHuffsizes(token.tableDHT[hufftableId].huffbits, WsqHelper.MAX_HUFFCOUNTS_WSQ);
                    BuildHuffcodes(hufftable);

                    /* this routine builds a set of three tables used in decoding */
                    /* the compressed buffer*/
                    GenDecodeTable(hufftable, maxcode, mincode, valptr, token.tableDHT[hufftableId].huffbits);

                    bitCount.value = 0;
                    marker.value = 0;
                }

                /* get next huffman category code from compressed input buffer stream */
                var nodeptr = DecodeDataMem(token, mincode, maxcode, valptr, token.tableDHT[hufftableId].huffvalues,
                    bitCount, marker, nextByte);
                /* nodeptr  pointers for decoding */

                if (nodeptr == -1)
                    continue;

                if (nodeptr > 0 && nodeptr <= 100)
                {
                    for (var n = 0; n < nodeptr; n++)
                        qdata[ip++] = 0; /* z run */
                }
                else if (nodeptr > 106 && nodeptr < 0xff)
                {
                    qdata[ip++] = nodeptr - 180;
                }
                else if (nodeptr == 101)
                {
                    qdata[ip++] = GetCNextbitsWSQ(token, marker, bitCount, 8, nextByte);
                }
                else if (nodeptr == 102)
                {
                    qdata[ip++] = -GetCNextbitsWSQ(token, marker, bitCount, 8, nextByte);
                }
                else if (nodeptr == 103)
                {
                    qdata[ip++] = GetCNextbitsWSQ(token, marker, bitCount, 16, nextByte);
                }
                else if (nodeptr == 104)
                {
                    qdata[ip++] = -GetCNextbitsWSQ(token, marker, bitCount, 16, nextByte);
                }
                else if (nodeptr == 105)
                {
                    var n = GetCNextbitsWSQ(token, marker, bitCount, 8, nextByte);
                    while (n-- > 0) qdata[ip++] = 0;
                }
                else if (nodeptr == 106)
                {
                    var n = GetCNextbitsWSQ(token, marker, bitCount, 16, nextByte);
                    while (n-- > 0) qdata[ip++] = 0;
                }
                else
                {
                    throw new SystemException("ERROR: huffman_decode_data_mem : Invalid code (" + nodeptr + ")");
                }
            }

            return qdata;
        }

        private static int GetCBlockHeader(Token token)
        {
            token.ReadShort(); /* block header size */
            return token.ReadByte();
        }

        private static HuffCode[] BuildHuffsizes(int[] huffbits, int maxHuffcounts)
        {
            var numberOfCodes = 1; /*the number codes for a given code size*/

            var huffcodeTable = new HuffCode[maxHuffcounts + 1];

            var tempSize = 0;
            for (var codeSize = 1; codeSize <= WsqHelper.MAX_HUFFBITS; codeSize++)
            {
                while (numberOfCodes <= huffbits[codeSize - 1])
                {
                    huffcodeTable[tempSize] = new HuffCode
                    {
                        size = codeSize
                    };
                    tempSize++;
                    numberOfCodes++;
                }

                numberOfCodes = 1;
            }

            huffcodeTable[tempSize] = new HuffCode
            {
                size = 0
            };

            return huffcodeTable;
        }

        private static void BuildHuffcodes(HuffCode[] huffcodeTable)
        {
            short tempCode = 0; /*used to construct code word*/
            var pointer = 0; /*pointer to code word information*/

            var tempSize = huffcodeTable[0].size;
            if (huffcodeTable[pointer].size == 0) return;

            do
            {
                do
                {
                    huffcodeTable[pointer].code = tempCode;
                    tempCode++;
                    pointer++;
                } while (huffcodeTable[pointer].size == tempSize);

                if (huffcodeTable[pointer].size == 0)
                    return;

                do
                {
                    tempCode <<= 1;
                    tempSize++;
                } while (huffcodeTable[pointer].size != tempSize);
            } while (huffcodeTable[pointer].size == tempSize);
        }

        private static void GenDecodeTable(HuffCode[] huffcodeTable, int[] maxcode, int[] mincode, int[] valptr,
            int[] huffbits)
        {
            for (var i = 0; i <= WsqHelper.MAX_HUFFBITS; i++)
            {
                maxcode[i] = 0;
                mincode[i] = 0;
                valptr[i] = 0;
            }

            var i2 = 0;
            for (var i = 1; i <= WsqHelper.MAX_HUFFBITS; i++)
            {
                if (huffbits[i - 1] == 0)
                {
                    maxcode[i] = -1;
                    continue;
                }

                valptr[i] = i2;
                mincode[i] = huffcodeTable[i2].code;
                i2 = i2 + huffbits[i - 1] - 1;
                maxcode[i] = huffcodeTable[i2].code;
                i2++;
            }
        }

        private static int DecodeDataMem(Token token, int[] mincode, int[] maxcode, int[] valptr, int[] huffvalues,
            IntRef bitCount, IntRef marker, IntRef nextByte)
        {
            var code = (short) GetCNextbitsWSQ(token, marker, bitCount, 1,
                nextByte); /* becomes a huffman code word  (one bit at a time)*/
            if (marker.value != 0)
                return -1;

            int inx;
            for (inx = 1; code > maxcode[inx]; inx++)
            {
                var tbits = GetCNextbitsWSQ(token, marker, bitCount, 1,
                    nextByte); /* becomes a huffman code word  (one bit at a time)*/
                code = (short) ((code << 1) + tbits);

                if (marker.value != 0)
                    return -1;
            }

            var inx2 = valptr[inx] + code - mincode[inx]; /*increment variables*/
            return huffvalues[inx2];
        }

        private static int GetCNextbitsWSQ(Token token, IntRef marker, IntRef bitCount, int bitsReq, IntRef nextByte)
        {
            if (bitCount.value == 0)
            {
                nextByte.value = token.ReadByte();

                bitCount.value = 8;
                if (nextByte.value == 0xFF)
                {
                    var code2 = token.ReadByte(); /*stuffed byte of buffer*/

                    if (code2 != 0x00 && bitsReq == 1)
                    {
                        marker.value = (nextByte.value << 8) | code2;
                        return 1;
                    }

                    if (code2 != 0x00) throw new SystemException("ERROR: getCNextbitsWSQ : No stuffed zeros.");
                }
            }

            int bits; /*bits of current buffer byte requested*/

            if (bitsReq <= bitCount.value)
            {
                bits = (nextByte.value >> (bitCount.value - bitsReq)) & WsqHelper.BITMASK[bitsReq];
                bitCount.value -= bitsReq;
                nextByte.value &= WsqHelper.BITMASK[bitCount.value];
            }
            else
            {
                var bitsNeeded = bitsReq - bitCount.value; /*additional bits required to finish request*/
                bits = nextByte.value << bitsNeeded;
                bitCount.value = 0;
                var tbits = GetCNextbitsWSQ(token, marker, bitCount, bitsNeeded,
                    nextByte); /*bits of current buffer byte requested*/
                bits |= tbits;
            }

            return bits;
        }

        private static float[] Unquantize(Token token, int[] sip, int width, int height)
        {
            var fip = new float[width * height]; /* floating point image */

            if (token.tableDQT.dqtDef != 1)
                throw new SystemException("ERROR: unquantize : quantization table parameters not defined!");

            var binCenter = token.tableDQT.binCenter; /* quantizer bin center */

            var sptr = 0;
            for (var cnt = 0; cnt < WsqHelper.NUM_SUBBANDS; cnt++)
            {
                if (token.tableDQT.qBin[cnt] == 0.0)
                    continue;

                var fptr = token.qtree[cnt].y * width + token.qtree[cnt].x;

                for (var row = 0; row < token.qtree[cnt].leny; row++, fptr += width - token.qtree[cnt].lenx)
                for (var col = 0; col < token.qtree[cnt].lenx; col++)
                {
                    if (sip[sptr] == 0)
                        fip[fptr] = 0.0f;
                    else if (sip[sptr] > 0)
                        fip[fptr] = token.tableDQT.qBin[cnt] * (sip[sptr] - binCenter) +
                                    token.tableDQT.zBin[cnt] / 2.0f;
                    else if (sip[sptr] < 0)
                        fip[fptr] = token.tableDQT.qBin[cnt] * (sip[sptr] + binCenter) -
                                    token.tableDQT.zBin[cnt] / 2.0f;
                    else
                        throw new SystemException("ERROR : unquantize : invalid quantization pixel value");

                    fptr++;
                    sptr++;
                }
            }

            return fip;
        }

        private static void WsqReconstruct(Token token, float[] fdata, int width, int height)
        {
            if (token.tableDTT.lodef != 1)
                throw new SystemException("ERROR: wsq_reconstruct : Lopass filter coefficients not defined");

            if (token.tableDTT.hidef != 1)
                throw new SystemException("ERROR: wsq_reconstruct : Hipass filter coefficients not defined");

            var numPix = width * height;
            /* Allocate temporary floating point pixmap. */
            var fdataTemp = new float[numPix];

            /* Reconstruct floating point pixmap from wavelet subband buffer. */
            for (var node = WsqHelper.W_TREELEN - 1; node >= 0; node--)
            {
                var fdataBse = token.wtree[node].y * width + token.wtree[node].x;
                JoinLets(fdataTemp, fdata, 0, fdataBse, token.wtree[node].lenx, token.wtree[node].leny,
                    1, width,
                    token.tableDTT.hifilt, token.tableDTT.hisz,
                    token.tableDTT.lofilt, token.tableDTT.losz,
                    token.wtree[node].invcl);
                JoinLets(fdata, fdataTemp, fdataBse, 0, token.wtree[node].leny, token.wtree[node].lenx,
                    width, 1,
                    token.tableDTT.hifilt, token.tableDTT.hisz,
                    token.tableDTT.lofilt, token.tableDTT.losz,
                    token.wtree[node].invrw);
            }
        }

        private static void JoinLets(
            IList<float> newdata,
            IReadOnlyList<float> olddata,
            int newIndex,
            int oldIndex,
            int len1, /* temporary length parameters */
            int len2,
            int pitch, /* pitch gives next row_col to filter */
            int stride, /*           stride gives next pixel to filter */
            IList<float> hi,
            int hsz, /* NEW */
            IReadOnlyList<float> lo, /* filter coefficients */
            int lsz, /* NEW */
            int inv) /* spectral inversion? */
        {
            int cl_rw; /* pixel counter and column/row counter */
            int i; /* if "scanline" is even or odd and */
            int loc, hoc;
            int hlen, llen;
            int olle, ohle, olre, ohre;
            int lotap;
            int hotap;
            int asym, fhre = 0, ofhre;
            float ssfac;

            var da_ev = len2 % 2;
            var fi_ev = lsz % 2;
            var pstr = stride;
            var nstr = -pstr;
            if (da_ev != 0)
            {
                llen = (len2 + 1) / 2;
                hlen = llen - 1;
            }
            else
            {
                llen = len2 / 2;
                hlen = llen;
            }

            if (fi_ev != 0)
            {
                asym = 0;
                ssfac = 1.0f;
                ofhre = 0;
                loc = (lsz - 1) / 4;
                hoc = (hsz + 1) / 4 - 1;
                lotap = (lsz - 1) / 2 % 2;
                hotap = (hsz + 1) / 2 % 2;
                if (da_ev != 0)
                {
                    olle = 0;
                    olre = 0;
                    ohle = 1;
                    ohre = 1;
                }
                else
                {
                    olle = 0;
                    olre = 1;
                    ohle = 1;
                    ohre = 0;
                }
            }
            else
            {
                asym = 1;
                ssfac = -1.0f;
                ofhre = 2;
                loc = lsz / 4 - 1;
                hoc = hsz / 4 - 1;
                lotap = lsz / 2 % 2;
                hotap = hsz / 2 % 2;
                if (da_ev != 0)
                {
                    olle = 1;
                    olre = 0;
                    ohle = 1;
                    ohre = 1;
                }
                else
                {
                    olle = 1;
                    olre = 1;
                    ohle = 1;
                    ohre = 1;
                }

                if (loc == -1)
                {
                    loc = 0;
                    olle = 0;
                }

                if (hoc == -1)
                {
                    hoc = 0;
                    ohle = 0;
                }

                for (i = 0; i < hsz; i++)
                    hi[i] *= -1.0F;
            }

            for (cl_rw = 0; cl_rw < len1; cl_rw++)
            {
                var limg = newIndex + cl_rw * pitch;
                var himg = limg;
                newdata[himg] = 0.0f;
                newdata[himg + stride] = 0.0f;
                int lopass; /* lo/hi pass image pointers */
                int hipass; /* lo/hi pass image pointers */
                if (inv != 0)
                {
                    hipass = oldIndex + cl_rw * pitch;
                    lopass = hipass + stride * hlen;
                }
                else
                {
                    lopass = oldIndex + cl_rw * pitch;
                    hipass = lopass + stride * llen;
                }

                var lp0 = lopass;
                var lp1 = lp0 + (llen - 1) * stride;
                var lspx = lp0 + loc * stride;
                var lspxstr = nstr;
                var lstap = lotap;
                var lle2 = olle;
                var lre2 = olre;

                var hp0 = hipass;
                var hp1 = hp0 + (hlen - 1) * stride;
                var hspx = hp0 + hoc * stride;
                var hspxstr = nstr;
                var hstap = hotap;
                var hle2 = ohle;
                var hre2 = ohre;
                var osfac = ssfac;

                int pix; /* pixel counter and column/row counter */
                int hle;
                int tap;
                int hpx;
                int lle;
                int lpxstr;
                int hpxstr;
                int lre;
                float sfac;
                int hre;
                int lpx;
                for (pix = 0; pix < hlen; pix++)
                {
                    for (tap = lstap; tap >= 0; tap--)
                    {
                        lle = lle2;
                        lre = lre2;
                        lpx = lspx;
                        lpxstr = lspxstr;

                        newdata[limg] = olddata[lpx] * lo[tap];
                        for (i = tap + 2; i < lsz; i += 2)
                        {
                            if (lpx == lp0)
                            {
                                if (lle != 0)
                                {
                                    lpxstr = 0;
                                    lle = 0;
                                }
                                else
                                {
                                    lpxstr = pstr;
                                }
                            }

                            if (lpx == lp1)
                            {
                                if (lre != 0)
                                {
                                    lpxstr = 0;
                                    lre = 0;
                                }
                                else
                                {
                                    lpxstr = nstr;
                                }
                            }

                            lpx += lpxstr;
                            newdata[limg] += olddata[lpx] * lo[i];
                        }

                        limg += stride;
                    }

                    if (lspx == lp0)
                    {
                        if (lle2 != 0)
                        {
                            lspxstr = 0;
                            lle2 = 0;
                        }
                        else
                        {
                            lspxstr = pstr;
                        }
                    }

                    lspx += lspxstr;
                    lstap = 1;

                    for (tap = hstap; tap >= 0; tap--)
                    {
                        hle = hle2;
                        hre = hre2;
                        hpx = hspx;
                        hpxstr = hspxstr;
                        fhre = ofhre;
                        sfac = osfac;

                        for (i = tap; i < hsz; i += 2)
                        {
                            if (hpx == hp0)
                            {
                                if (hle != 0)
                                {
                                    hpxstr = 0;
                                    hle = 0;
                                }
                                else
                                {
                                    hpxstr = pstr;
                                    sfac = 1.0f;
                                }
                            }

                            if (hpx == hp1)
                            {
                                if (hre != 0)
                                {
                                    hpxstr = 0;
                                    hre = 0;
                                    if (asym != 0 && da_ev != 0)
                                    {
                                        hre = 1;
                                        fhre--;
                                        sfac = fhre;
                                        if (sfac == 0.0)
                                            hre = 0;
                                    }
                                }
                                else
                                {
                                    hpxstr = nstr;
                                    if (asym != 0)
                                        sfac = -1.0f;
                                }
                            }

                            newdata[himg] += olddata[hpx] * hi[i] * sfac;
                            hpx += hpxstr;
                        }

                        himg += stride;
                    }

                    if (hspx == hp0)
                    {
                        if (hle2 != 0)
                        {
                            hspxstr = 0;
                            hle2 = 0;
                        }
                        else
                        {
                            hspxstr = pstr;
                            osfac = 1.0f;
                        }
                    }

                    hspx += hspxstr;
                    hstap = 1;
                }

                lstap = da_ev != 0 ? lotap != 0 ? 1 : 0 : lotap != 0 ? 2 : 1;

                for (tap = 1; tap >= lstap; tap--)
                {
                    lle = lle2;
                    lre = lre2;
                    lpx = lspx;
                    lpxstr = lspxstr;

                    newdata[limg] = olddata[lpx] * lo[tap];
                    for (i = tap + 2; i < lsz; i += 2)
                    {
                        if (lpx == lp0)
                        {
                            if (lle != 0)
                            {
                                lpxstr = 0;
                                lle = 0;
                            }
                            else
                            {
                                lpxstr = pstr;
                            }
                        }

                        if (lpx == lp1)
                        {
                            if (lre != 0)
                            {
                                lpxstr = 0;
                                lre = 0;
                            }
                            else
                            {
                                lpxstr = nstr;
                            }
                        }

                        lpx += lpxstr;
                        newdata[limg] += olddata[lpx] * lo[i];
                    }

                    limg += stride;
                }

                if (da_ev != 0)
                {
                    hstap = hotap != 0 ? 1 : 0;

                    if (hsz == 2)
                    {
                        hspx -= hspxstr;
                        fhre = 1;
                    }
                }
                else
                {
                    hstap = hotap != 0 ? 2 : 1;
                }

                for (tap = 1; tap >= hstap; tap--)
                {
                    hle = hle2;
                    hre = hre2;
                    hpx = hspx;
                    hpxstr = hspxstr;
                    sfac = osfac;
                    if (hsz != 2)
                        fhre = ofhre;

                    for (i = tap; i < hsz; i += 2)
                    {
                        if (hpx == hp0)
                        {
                            if (hle != 0)
                            {
                                hpxstr = 0;
                                hle = 0;
                            }
                            else
                            {
                                hpxstr = pstr;
                                sfac = 1.0f;
                            }
                        }

                        if (hpx == hp1)
                        {
                            if (hre != 0)
                            {
                                hpxstr = 0;
                                hre = 0;
                                if (asym != 0 && da_ev != 0)
                                {
                                    hre = 1;
                                    fhre--;
                                    sfac = fhre;
                                    if (sfac == 0.0)
                                        hre = 0;
                                }
                            }
                            else
                            {
                                hpxstr = nstr;
                                if (asym != 0)
                                    sfac = -1.0f;
                            }
                        }

                        newdata[himg] += olddata[hpx] * hi[i] * sfac;
                        hpx += hpxstr;
                    }

                    himg += stride;
                }
            }

            if (fi_ev == 0)
                for (i = 0; i < hsz; i++)
                    hi[i] *= -1.0F;
        }

        private static byte[] ConvertImage2Byte(float[] img, int width, int height, float mShift, float rScale)
        {
            var data = new byte[width * height];

            var idx = 0;
            for (var r = 0; r < height; r++)
            for (var c = 0; c < width; c++)
            {
                var pixel = img[idx] * rScale + mShift;
                pixel += 0.5F;

                if (pixel < 0.0)
                    data[idx] = 0; /* neg pix poss after quantization */
                else if (pixel > 255.0)
                    data[idx] = 255;
                else
                    data[idx] = (byte) pixel;

                idx++;
            }

            return data;
        }
    }
}