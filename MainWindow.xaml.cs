using Microsoft.Win32;
using ScottPlot.Drawing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WpfApp1
{

    // MLX90640 RAM (0x400～0x73F)とControl register1,Status register
    //         
    public class RAMData
    {
        public string index { get; set; }   //  frameData[index]

        public string adrs { get; set; }   // RAM アドレス

        public ushort data { get; set; }   // データ(符号なし 16bit)

        public string data_str { get; set; }    // データ 10進数の文字列

        public string descript { get; set; }   // 説明
    }


    // 測定対象物の温度
    public class ToData
    {
        public int pixnum { get; set; }   // ピクセル番号 ( 1 ～ 768 )

        public float To { get; set; }    // 温度 To 


    }


    // MLX90640 EEPROM(0x2400～0x273F)
    // クラス　CalibDataの定義
    // メンバー: String index
    //           String adrs 
    //           short  data
    //           String data_str
    //           String descript
    //         
    public class CalibData
    {
        public string index { get; set; }   //  eeData[index]
        public string adrs { get; set; }   // 読み出しアドレス

        public short data { get; set; }   // データ (符号付き 16bit) 

        public string data_str { get; set; }    // データ 10進数の文字列

        public string descript { get; set; }   // 説明
    }


    // メンバー
    //     short KVdd;             
    //     short vdd25;
    //     float KvPTAT;
    //     float KtPTAT;
    //    ushort vPTAT25;
    //    float alphaPTAT;



    public partial class MainWindow : Window
    {
        public static byte[] sendBuf;          // 送信バッファ   
        public static int sendByteLen;         //　送信データのバイト数

        public static byte[] rcvBuf;           // 受信バッファ
        public static int srcv_pt;             // 受信データ格納位置

        public static DateTime receiveDateTime;           // 受信完了日時

        public static DispatcherTimer SendIntervalTimer;  // タイマ　モニタ用　電文送信間隔   
        DispatcherTimer RcvWaitTimer;                   　// タイマ　受信待ち用 

    

        public static ushort send_msg_cnt;              // 送信数 
        public static ushort disp_msg_cnt_max;          // 送受信文の表示最大数

        public static int commlog_window_cnt;           // 通信ログ用ウィンドウの表示個数
        public static int heatmap_window_cnt;           // ヒートマップ用ウィンドウの表示個数
        public static int rowcol_window_cnt;            // 温度の行列表示用ウィンドウの表示個数 

        public static ObservableCollection<RAMData> ram_list;     // MLX90640 RAMデータ(ピクセルの生データ) 
        public static ObservableCollection<CalibData> calib_list; // MLX90640 EEPROMデータ(パラメータデータ)
       
        public static ObservableCollection<ToData> to_list;       // 測定対象物の温度(To) Sub page0とSub page1
        public static ObservableCollection<ToData> to_list_0;     // 測定対象物の温度(To) Sub page0用
        public static ObservableCollection<ToData> to_list_1;     //       :              Sub page1用   

        public static short[] eeData;   // MLX90640 EEPROM(0x2400～0x273f)のデータ
        public static ushort[] frameData; // MLX90640 RAM(0x400～0x73F)のデータ　+ (Control register1)  +  (statusRegister & 0x0001);

        public static byte colorbar_type;   // カラーバーの種類 0=turbo, 1=bluse, 2=grayscale　

        public static uint rcvmsg_proc_cnt;  // RcvMsgProc()の実行回数    (デバック用)
        public static byte rcvmsg_proc_flg;  // RcvMsgProc()の実行中 = 1 (デバック用)                           

        MLX90640    mLX;                // MLX90640クラス

        Heatmap WinHeatmap;             // Heatmap

        DispRowCol WinDispRowCol;


        public MainWindow()
        {
            InitializeComponent();

            ConfSerial.serialPort = new SerialPort();    // シリアルポートのインスタンス生成

            ConfSerial.serialPort.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);  // データ受信時のイベント処理

            sendBuf = new byte[2048];     // 送信バッファ領域  serialPortのWriteBufferSize =2048 byte(デフォルト)
            rcvBuf = new byte[4096];      // 受信バッファ領域   SerialPort.ReadBufferSize = 4096 byte (デフォルト)
                                          
            disp_msg_cnt_max = 1000;        // 送受信文の表示最大数

            SendIntervalTimer = new System.Windows.Threading.DispatcherTimer();　　// タイマーの生成(定周期モニタ用)
            SendIntervalTimer.Tick += new EventHandler(SendIntervalTimer_Tick);  // タイマーイベント
           // SendIntervalTimer.Interval = new TimeSpan(0, 0, 0, 0, 1000);       // タイマーイベント発生間隔 1sec(コマンド送信周期)
            SendIntervalTimer.Interval = new TimeSpan(0, 0, 0, 0, 500);          // タイマーイベント発生間隔 500msec(コマンド送信周期)
            

            RcvWaitTimer = new System.Windows.Threading.DispatcherTimer();　 // タイマーの生成(受信待ちタイマ)
            RcvWaitTimer.Tick += new EventHandler(RcvWaitTimer_Tick);        // タイマーイベント
            RcvWaitTimer.Interval = new TimeSpan(0, 0, 0, 0, 1000);          // タイマーイベント発生間隔 (受信待ち時間)

            ram_list = new ObservableCollection<RAMData>();         //  クラス RAMDataのコレクションをデータバインディングするため、ObservableCollectionで生成
            this.Pixel_File_DataGrid.ItemsSource = ram_list;        //  データグリッド ( Pixel_File_DataGrid )のソース指定, MLX90640 RAMデータ

            calib_list = new ObservableCollection<CalibData>();     // 
            this.Calib_File_DataGrid.ItemsSource = calib_list;      //  MLX90640 EEPROMデータ

            to_list = new ObservableCollection<ToData>();
            this.To_DataGrid.ItemsSource = to_list;                 // MLX90640 To(対象物の測定温度）Subpage0とSubpage1 

            to_list_0 = new ObservableCollection<ToData>();
         //   this.To_DataGrid_0.ItemsSource = to_list_0;

            to_list_1 = new ObservableCollection<ToData>();
          //  this.To_DataGrid_1.ItemsSource = to_list_1;


            eeData = new short[832];        // MLX90640 EEPROM(0x2400～0x273f)のデータ, (0x33f = 831)

            frameData = new ushort[834];    // MLX90640 RAM(0x400～0x73f)のデータと(Control register1)と (statusRegister & 0x0001)
           
            mLX = new MLX90640();           // MLX90640クラスの生成

            rcvmsg_proc_cnt = 0;            // RcvMsgProc()の実行回数 (デバック用)
            rcvmsg_proc_flg = 0;            

        }



        // 定周期モニタ用 
        //  rd_mlx_em_flg = 0 の場合、 MLX90640のRAM読み出しコマンドのセット
        //                = 1 の場合、 放射率、熱電対の温度読み出しコマンドのセット
        private void SendIntervalTimer_Tick(object sender, EventArgs e)
        {
           
            mlx90640_read_ram_cmd_set(); // MLX90640のRAM読み出しコマンドのセット

            bool fok = send_disp_data();       // データ送信
        }


        // 送信後、1000msec以内に受信文が得られないと、受信エラー
        //  
        private void RcvWaitTimer_Tick(object sender, EventArgs e)
        {

            RcvWaitTimer.Stop();        // 受信監視タイマの停止

            StatusTextBlock.Text = "Receive time out";
        }



        private delegate void DelegateFn();

        // データ受信時のイベント処理
        private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            if ( rcvmsg_proc_flg == 1)     //  RcvMsgProc()の実行中の場合、処理しない
            {
                return;
            }

            int id = System.Threading.Thread.CurrentThread.ManagedThreadId;
            Console.WriteLine("DataReceivedHandlerのスレッドID : " + id);

            int rd_num = ConfSerial.serialPort.BytesToRead;       // 受信データ数

            ConfSerial.serialPort.Read(rcvBuf, srcv_pt, rd_num);   // 受信データの読み出し

            srcv_pt = srcv_pt + rd_num;     // 次回の保存位置

            int rcv_total_byte = 0;

            if (rcvBuf[0] == 0xb0)             // MLX90640　EEPROM読み出しコマンド(0x30)のレスポンスの場合
            {
                rcv_total_byte = 1667;
            }
            else if (rcvBuf[0] == 0xb1)         // MLX90640　RAM読み出しコマンド(0x31)のレスポンスの場合
            {
                rcv_total_byte = 1687;
            }



            if (srcv_pt == rcv_total_byte)  // 最終データ受信済み 
            {
                RcvWaitTimer.Stop();        // 受信監視タイマー　停止

                receiveDateTime = DateTime.Now;   // 受信完了時刻を得る

                rcvmsg_proc_flg = 1;       // RcvMsgProc()の実行中

                Dispatcher.BeginInvoke(new DelegateFn(RcvMsgProc)); // Delegateを生成して、RcvMsgProcを開始   (表示は別スレッドのため)
            }

        }


        //  
        //  最終データ受信後の処理
        //  表示、補正データの展開、温度計算
        //  
        private void RcvMsgProc()
        {
            if (rcvBuf[0] == 0xb0)     // MLX90640　EEPROM読み出しコマンド(0x30)のレスポンスの場合
            {
                Disp_eeprom_data();   //  EEPROMデータの表示

                // calib_list[]をeeData[]へコピー
                for (int i = 0; i < 832; i++)           // MLX90640 EEPROM(0x2400～0x273f)のデータ, (0x33f = 831)
                {
                    eeData[i] = calib_list[i].data;
                }

                mLX.Extract_Parameters();      // 各パラメータのExtract

            }

            else if (rcvBuf[0] == 0xb1)     // MLX90640　RAM読み出しコマンド(0x31)のレスポンスの場合
            {
                Disp_ram_data();    //  RAMデータの表示
                                    // ram_list[]をframeData[]へコピー
                for (int i = 0; i < 834; i++)           // MLX90640 EEPROM(0x400～0x73f)のデータと (Control register1)と (statusRegister & 0x0001)
                {
                    frameData[i] = ram_list[i].data;
                }
                frameData[833] = (ushort)(frameData[833] & 0x0001);   // Status register(0x8000)のbit0:直近に測定されたサブページ番号

                float emissivity = 0.95f;           // 放射率


                float tr = mLX.GetTa();          // tr(reflected temperature): 屋内で使用する場合は、センサ周囲温度(Ta)を使用
                                                 //  float tr = mLX.GetTa() - 8.0f; 屋外で使用する場合は、センサ周囲温度(Ta) - 8.0
                                                 //
                                                 // MLX90640 32x24 IR array driver (Rev.1 - October 31,2022) (page 15)
                                                 // mlx_tr : reflected temperature defined by the user. 
                                                 //  If the object emissivity is less than 1, there might be some temperature reflected from the object. 
                                                 // In order for this to be compensated the user should input this reflected temperature. 
                                                 // The sensor ambient temperature could be used, but some shift depending on the enclosure might be needed. 
                                                 // For a MLX90640 in the open air the shift is -8°C.


                mLX.CalculateTo(emissivity, tr);  // Toの計算

                bool fg_ok = Merge_to_list();                    // to_list_0とto_list1をマージして to_list作成

               if (fg_ok)       // to_list 作成済みの場合 (subpate0とsubpate1の両方のピクセルの温度計算済み)
               {
                    ScottPlot_Button.IsEnabled = true;  // ヒートマップ表示ボタンの有効
                    Temp_Button.IsEnabled = true;

                                                          // 温度の最大値と最小値を求める
                    float t_min = to_list.Where(t => ((t.pixnum >= 1) && (t.pixnum <= 768))).Min(t => t.To);       //  to_list ピクセル番号 ( 1 ～ 768 )内の最低温度 
                    float t_max = to_list.Where(t => ((t.pixnum >= 1) && (t.pixnum <= 768))).Max(t => t.To);       //  to_list ピクセル番号 ( 1 ～ 768 )内の最高温度

                    TBLK_MAX_TEMP.Text = t_max.ToString("F1");  // 最大値表示
                    TBLK_MIN_TEMP.Text = t_min.ToString("F1");  // 最小値表示 

                    float t_pixel_center = to_list[383].To;     // pixel(1～768)の中央の温度, to_list[0].Toは pixel番号1の温度

                    TBLK_CENTER_PIXEL_TEMP.Text = t_pixel_center.ToString("F1");

                }


                if (rowcol_window_cnt == 1) {       // 行と列による温度表示Windowが表示済みの場合
                    WinDispRowCol.Disp_To_RowCol();    // 温度の表示
                }


                if (heatmap_window_cnt == 1)        // ヒートマップのWindow表示済みの場合
                {
                    WinHeatmap.Disp_Heatmap();   　// ヒートマップの表示 カラーバー表示無し
                }
            }


            if (CommLog.rcvframe_list != null)
            {
                CommLog.rcvmsg_disp();          // 受信データの表示       
            }

            rcvmsg_proc_cnt++;         // RcvMsgProc()の実行回数　インクリメント
            
            rcvmsg_proc_flg = 0;       // RcvMsgProc()の完了

        }



        // to_list_0とto_list1をマージして to_list作成
        // to_list_0 :サブページ0のピクセル番号とそのピクセルの温度
        // to_list_1 :サブページ1のピクセル番号とそのピクセルの温度
        //            (ピクセル番号:1～768)
        private bool Merge_to_list()
        {
           
           if ( to_list_0.Count != 384 ) { return false; }
           if ( to_list_1.Count != 384 ) { return false; }


            to_list.Clear();    // to_listのクリア

            int set_flg = 1;    // to_list_0 , to_list_1の順で to_listへ格納

            for ( int i = 0; i < 384 ; i++ )
            {
                 ToData toData = new ToData();

                if (i % 16 == 0)
                {
                    if (set_flg == 1)
                    {
                        set_flg = 0;     // to_list_0 , to_list_1の順で to_listへ格納
                    }
                    else
                    {
                        set_flg = 1;    // to_list_1 , to_list_0の順で to_listへ格納 
                    }
                }


                if (set_flg == 0)
                {
                    toData = to_list_0[i];
                    to_list.Add(toData);   // to_listへ追加

                    toData = to_list_1[i];
                    to_list.Add(toData);     // to_listへ追加
                }
                else
                {
                    toData = to_list_1[i];
                    to_list.Add(toData);     // to_listへ追加

                    toData = to_list_0[i];
                    to_list.Add(toData);   // to_listへ追加
                }

            }

            return true;

        }

        // マイコンから読み出した　MLX90640のRAMデータ及び、熱電対の温度表示
        // 受信データ:
        //     rcvBuf[0] : 0xb1 (コマンドに対するレスポンス)
        //     rcvBuf[1] : アドレス(0x400)のデータ(上位バイト側)
        //     rcvBuf[2] : アドレス(0x400)のデータ(下位バイト側)
        //     rcvBuf[3] : アドレス(0x401)のデータ(上位バイト側)
        //     rcvBuf[4] : アドレス(0x401)のデータ(下位バイト側)
        //          :                  :
        //          :                  :
        //     rcvBuf[1663]: アドレス(0x73F)のデータ(上位バイト側)
        //     rcvBuf[1664]: アドレス(0x73F)のデータ(下位バイト側)
        //
        //     rcvBuf[1665]:Control register 1 (0x800d)(上位バイト側)
        //     rcvBuf[1666]:Control register 1 (0x800d)(下位バイト側)
        //     rcvBuf[1667]:Status register (0x8000)(上位バイト側)
        //     rcvbuf[1668]:Status register (0x8000)(下位バイト側)
        //     rcvBuf[1669] : 放射率(Em)  (下位バイト側) 100倍した値(例:Em= 0.95ならば950を返す)
        //     rcvBuf[1670] :   :         (上位バイト側)
        //     rcvBuf[1671] : 周囲温度(Ta)(下位バイト側) 10倍した値 (例:Ta=23.5ならば235を返す)
        //     rcvBuf[1672] :   :         (上位バイト側)
        //     rcvBuf[1673] : 反射温度(Tr)(下位バイト側) 10倍した値
        //     rcvBuf[1674] :   :         (上位バイト側)
        //     rcvBuf[1675] : 熱電対 CH1温度  (下位バイト側)
        //     rcvBuf[1676] :    :            (上位バイト側)
        //     rcvBuf[1677] : 熱電対 CH2温度 (下位バイト側)  
        //     rcvBuf[1678] :    :	    (上位バイト側)
        //     rcvBuf[1679] : 熱電対 CH3温度 (下位バイト側) 
        //     rcvBuf[1680] :    :           (上位バイト側)
        //     rcvBuf[1681] : 熱電対 CH4温度 (下位バイト側)  
        //     rcvBuf[1682] :    :　　　　　 (上位バイト側)
        //     rcvBuf[1683] : 基準接点温度(CJT)  (下位バイト側)  
        //     rcvBuf[1684] :    :               (上位バイト側)
        //     rcvBuf[1685]: CRC(上位バイト側)
        //     rcvBuf[1686]: CRC(下位バイト側)
        //
        private void Disp_ram_data()
        {
            Int16 dt;
            UInt16 crc_cd;
            float em, ta, tr;
            float ch1, ch2, ch3, ch4, cjt;

            crc_cd = CRC_rcvBuf_Cal(1687);         // 全データのCRC計算             

            if (crc_cd != 0)
            {
                AlarmTextBlock.Text = "Receive CRC Err.";
                SendIntervalTimer.Stop();     // データ収集用コマンド送信タイマー停止
                return;
            }
            else
            {
                AlarmTextBlock.Text = "";
            }

            ram_list.Clear();        // クリア

            int pt = 0;
            for (int i = 1; i <= 1667; i = i + 2)
            {
                RAMData ramdata = new RAMData();

                ramdata.index = pt.ToString();
                ramdata.adrs = "0x" + (pt + 0x400).ToString("x4");  // アドレス 16進数

                ushort rdata = (ushort)((rcvBuf[i] << 8) | (rcvBuf[i + 1])); // データ(16進数)

                ramdata.data = rdata;

                ramdata.data_str = rdata.ToString("d");  // データ(10進数)

                ramdata.descript = "";

                ram_list.Add(ramdata);   // データの追加

                pt++;
            }

            pt = 1669;
            dt = BitConverter.ToInt16(rcvBuf, pt);   // rcvBuf[2]から int16へ
            em = (float)(dt / 100.0);
            TBLK_EM.Text = em.ToString("F2");       // Emissivity

            dt = BitConverter.ToInt16(rcvBuf, pt+2);   // rcvBuf[4]から int16へ
            ta = (float)(dt / 10.0);
            TBLK_TA.Text = ta.ToString("F1");       // ambient temperature

            dt = BitConverter.ToInt16(rcvBuf, pt+4);   // rcvBuf[6]から int16へ
            tr = (float)(dt / 10.0);
            TBLK_TR.Text = tr.ToString("F1");       // reflected temperature

            dt = BitConverter.ToInt16(rcvBuf, pt+6);   // rcvBuf[8]から int16へ
            ch1 = (float)(dt / 10.0);
            TBLK_CH1.Text = ch1.ToString("F1");     // CH1

            dt = BitConverter.ToInt16(rcvBuf, pt+8);  // rcvBuf[10]から int16へ
            ch2 = (float)(dt / 10.0);
            TBLK_CH2.Text = ch2.ToString("F1");     // CH2

            dt = BitConverter.ToInt16(rcvBuf, pt+10);  // rcvBuf[12]から int16へ
            ch3 = (float)(dt / 10.0);
            TBLK_CH3.Text = ch3.ToString("F1");     // CH3

            dt = BitConverter.ToInt16(rcvBuf, pt+12);  // rcvBuf[14]から int16へ
            ch4 = (float)(dt / 10.0);
            TBLK_CH4.Text = ch4.ToString("F1");     // CH4

            dt = BitConverter.ToInt16(rcvBuf, pt+14);  // rcvBuf[16]から int16へ
            cjt = (float)(dt / 10.0);
            TBLK_CJT.Text = cjt.ToString("F1");     // CJT


        }



        // マイコンから読み出した　MLX90640のEEPROMデータを表示
        // 受信データ:
        //     rcvBuf[0] : 0xb0 (コマンドに対するレスポンス)
        //     rcvBuf[1] : アドレス(0x2400)のデータ(上位バイト側)
        //     rcvBuf[2] : アドレス(0x2400)のデータ(下位バイト側)
        //     rcvBuf[3] : アドレス(0x2401)のデータ(上位バイト側)
        //     rcvBuf[4] : アドレス(0x2401)のデータ(下位バイト側)
        //          :                  :
        //          :                  :
        //     rcvBuf[1663]: アドレス(0x273F)のデータ(上位バイト側)
        //     rcvBuf[1664]: アドレス(0x273F)のデータ(下位バイト側)
        //     rcvBuf[1665]: CRC(上位バイト側)
        //     rcvBuf[1666]: CRC(下位バイト側
        private void Disp_eeprom_data()
        {
            UInt16 crc_cd;

            crc_cd = CRC_rcvBuf_Cal(1667);         // 全データのCRC計算             

            if (crc_cd != 0)
            {
                AlarmTextBlock.Text = "Receive CRC Err.";
                SendIntervalTimer.Stop();     // データ収集用コマンド送信タイマー停止
                return;
            }
            else
            {
                AlarmTextBlock.Text = "";
            }

            calib_list.Clear();        // クリア

            int pt = 0;
            for ( int i = 1; i <= 1664; i = i + 2)
            {
                CalibData cbdata = new CalibData();

                cbdata.index = pt.ToString();
                cbdata.adrs = "0x" + (pt + 0x2400).ToString("x4");  // アドレス 16進数

                short rdata = (short)( (rcvBuf[i] << 8) | (rcvBuf[i + 1])); // データ(16進数)
                
                cbdata.data = rdata;

                cbdata.data_str = rdata.ToString("d");  // データ(10進数)

                cbdata.descript = "";

                calib_list.Add(cbdata);   // データの追加

                pt++;

            }
        }


        // CRCの計算 (受信バッファ用)
        // rcvBuf[]内のデータのCRCコードを作成
        //
        // 入力 size:データ数
        // 
        //  CRC-16 CCITT:
        //  多項式: X^16 + X^12 + X^5 + 1　
        //  初期値: 0xffff
        //  MSBファースト
        //  非反転出力
        // 
        private UInt16 CRC_rcvBuf_Cal(UInt16 size)
        {
            UInt16 crc;

            UInt16 i;

            crc = 0xffff;

            for (i = 0; i < size; i++)
            {
                crc = (UInt16)((crc >> 8) | ((UInt16)((UInt32)crc << 8)));

                crc = (UInt16)(crc ^ rcvBuf[i]);
                crc = (UInt16)(crc ^ (UInt16)((crc & 0xff) >> 4));
                crc = (UInt16)(crc ^ (UInt16)((crc << 8) << 4));
                crc = (UInt16)(crc ^ (((crc & 0xff) << 4) << 1));
            }

            return crc;

        }


        // CRCの計算 (送信バッファ用)
        // sendBuf[]内のデータのCRCコードを作成
        //
        // 入力 size:データ数
        // 
        //  CRC-16 CCITT:
        //  多項式: X^16 + X^12 + X^5 + 1　
        //  初期値: 0xffff
        //  MSBファースト
        //  非反転出力
        // 
        public static UInt16 CRC_sendBuf_Cal(UInt16 size)
        {
            UInt16 crc;

            UInt16 i;

            crc = 0xffff;

            for (i = 0; i < size; i++)
            {
                crc = (UInt16)((crc >> 8) | ((UInt16)((UInt32)crc << 8)));

                crc = (UInt16)(crc ^ sendBuf[i]);
                crc = (UInt16)(crc ^ (UInt16)((crc & 0xff) >> 4));
                crc = (UInt16)(crc ^ (UInt16)((crc << 8) << 4));
                crc = (UInt16)(crc ^ (((crc & 0xff) << 4) << 1));
            }

            return crc;

        }

        //  送信と送信データの表示
        // sendBuf[]のデータを、sendByteLenバイト　送信する
        // 戻り値  送信成功時: true
        //         送信失敗時: false

        public bool send_disp_data()
        {
            if (ConfSerial.serialPort.IsOpen == true)
            {
                srcv_pt = 0;                   // 受信データ格納位置クリア

                ConfSerial.serialPort.Write(sendBuf, 0, sendByteLen);     // データ送信

                if (CommLog.sendframe_list != null)
                {
                    CommLog.sendmsg_disp();          // 送信データの表示
                }

                send_msg_cnt++;              // 送信数インクリメント 

                RcvWaitTimer.Start();        // 受信監視タイマー　開始

                StatusTextBlock.Text = "";
                return true;
            }

            else
            {
                StatusTextBlock.Text = "Comm port closed !";
                SendIntervalTimer.Stop();
                return false;
            }

        }

      

        // 
        // MLX90640 EEPROM(0x2400～0x273F)読み出し コマンド(0x30)のセット
        // 
        private void mlx90640_read_eeprom_cmd_set()
        {
            UInt16 crc_cd;

            sendBuf[0] = 0x30;           // 送信コマンド  0x30 　
            sendBuf[1] = 0;              //　
            sendBuf[2] = 0;
            sendBuf[3] = 0;
            sendBuf[4] = 0;
            sendBuf[5] = 0;

            crc_cd = CRC_sendBuf_Cal(6);     // CRC計算

            sendBuf[6] = (Byte)(crc_cd >> 8); // CRCは上位バイト、下位バイトの順に送信
            sendBuf[7] = (Byte)(crc_cd & 0x00ff);

            sendByteLen = 8;                   // 送信バイト数

        
        }

       

        // 
        // MLX90640 RAM(0x400～0x73F)読み出し コマンド(0x31)
        // 
        private void  mlx90640_read_ram_cmd_set()
        {

            UInt16 crc_cd;

            sendBuf[0] = 0x31;           // 送信コマンド  0x31 　
            sendBuf[1] = 0;              //　
            sendBuf[2] = 0;
            sendBuf[3] = 0;
            sendBuf[4] = 0;
            sendBuf[5] = 0;

            crc_cd = CRC_sendBuf_Cal(6);     // CRC計算

            sendBuf[6] = (Byte)(crc_cd >> 8); // CRCは上位バイト、下位バイトの順に送信
            sendBuf[7] = (Byte)(crc_cd & 0x00ff);

            sendByteLen = 8;                   // 送信バイト数

        }


        


        // MLX90640 放射率、熱電対温度の読み出し コマンド(0x32)のセット
        private void mlx90640_em_tc_cmd_set()
        {

            UInt16 crc_cd;

            sendBuf[0] = 0x32;           // 送信コマンド  0x31 　
            sendBuf[1] = 0;              //　
            sendBuf[2] = 0;
            sendBuf[3] = 0;
            sendBuf[4] = 0;
            sendBuf[5] = 0;

            crc_cd = CRC_sendBuf_Cal(6);     // CRC計算

            sendBuf[6] = (Byte)(crc_cd >> 8); // CRCは上位バイト、下位バイトの順に送信
            sendBuf[7] = (Byte)(crc_cd & 0x00ff);

            sendByteLen = 8;                   // 送信バイト数

        }



        // モニタの「スタート」ボタンが押された場合の処理
        private void Start_Monitor_Button_Click(object sender, RoutedEventArgs e)
        {
            send_msg_cnt = 0;            // 送信数のクリア
            rcvmsg_proc_cnt = 0;            // RcvMsgProc()の実行回数 (デバック用)

            mlx90640_read_eeprom_cmd_set(); // MLX90640 EEPROM(0x2400～0x273F)読み出し コマンド(0x30)のセット

            bool fok = send_disp_data();       // データ送信


            SendIntervalTimer.Start();   // 定周期　送信用タイマの開始
        }

        //　モニタの「ストップ」ボタンが押された場合の処理
        private void Stop_Monitor_Button_Click(object sender, RoutedEventArgs e)
        {
            SendIntervalTimer.Stop();     // データ収集用コマンド送信タイマー停止
        }

        //　通信条件の設定 ダイアログを開く
        private void Serial_Button_Click(object sender, RoutedEventArgs e)
        {
            var window = new ConfSerial();
            window.Owner = this;
            window.ShowDialog();
        }

        // 通信メッセージ表示用のウィンドウを開く
        private void Comm_Log_Button_Click(object sender, RoutedEventArgs e)
        {
            if (commlog_window_cnt > 0) return;   // 既に開いている場合、リターン

            var window = new CommLog();

            window.Owner = this;   // Paraウィンドウの親は、このMainWindow

            window.Show();

            commlog_window_cnt++;     // カウンタインクリメント
        }



        // 　ヒートマップ　ウィンドウの表示
        private void Heat_map_Button_Click(object sender, RoutedEventArgs e)
        {
            if (to_list.Count != 768)   return;   // 温度が入っていない場合、リターン　
            if (heatmap_window_cnt > 0) return;   // 既に開いている場合、リターン

            WinHeatmap = new Heatmap();

            WinHeatmap.Owner = this;   // Paraウィンドウの親は、このMainWindow

            WinHeatmap.Show();

            WinHeatmap.Disp_XY_Axis();     // XY軸の表示
            WinHeatmap.Disp_Heatmap();   // ヒートマップの表示 (カラーバー表示あり)

            heatmap_window_cnt++;     // カウンタインクリメント
        }

        //  ピクセルの温度を行(row)と列(col)で表示
        //
        //      <--------     col      --------->
        //       32  31 30  .......         3 2 1
        // row 1
        //     2
        //     3
        //     : 
        //     :
        //    24
        //
        private void Disp_RC_Click(object sender, RoutedEventArgs e)
        {
            if (to_list.Count != 768) return;   // 温度が入っていない場合、リターン　
            if (rowcol_window_cnt > 0) return;

            WinDispRowCol = new DispRowCol();
            WinDispRowCol.Owner = this;

            WinDispRowCol.Show();

            WinDispRowCol.Disp_To_RowCol();    // 温度の表示

            rowcol_window_cnt++;


        }
        
        // カラーバー変更　ラジオボタンが押された場合
        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (RB_Turbo.IsChecked == true)
            {
                colorbar_type = 0; //  Turbo
            }
            else if (RB_Blues.IsChecked == true)
            {
                colorbar_type = 1; // Blues
            }
            else if (RB_Grayscale.IsChecked == true)
            {
                colorbar_type = 2; // Grayscale
            }
        }
    }
}
