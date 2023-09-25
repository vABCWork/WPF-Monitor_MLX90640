using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WpfApp1
{

    // MLX90640 温度計算用のパラメータ
    // 

    public class mlx90640Para
    {
        public short kVdd { get; set; }
        public short vdd25 { get; set; }
        public float KvPTAT { get; set; }
        public float KtPTAT { get; set; }
        public ushort vPTAT25 { get; set; }
        public float alphaPTAT { get; set; }
        public short gainEE { get; set; }
        public float tgc { get; set; }
        public float cpKv { get; set; }
        public float cpKta { get; set; }
        public byte resolutionEE { get; set; }
        public byte calibrationModeEE { get; set; }
        public float KsTa { get; set; }
        public float[] ksTo { get; set; }
        public short[] ct { get; set; }
        public ushort[] alpha { get; set; }
        public byte alphaScale { get; set; }
        public short[] offset { get; set; }
        public sbyte[] kta { get; set; }
        public byte ktaScale { get; set; }
        public sbyte[] kv { get; set; }
        public byte kvScale { get; set; }
        public float[] cpAlpha { get; set; }
        public short[] cpOffset { get; set; }
        public float[] ilChessC { get; set; }
        public ushort[] brokenPixels { get; set; }
        public ushort[] outlierPixels { get; set; }
    }

    /// <summary>
    /// MLX90640.xaml の相互作用ロジック
    /// </summary>
    public partial class MLX90640 : Window
    {

        // MLX90640 温度計算用
        public static mlx90640Para mlx90640;     // 温度計算用パラメータ

        public static float SCALEALPHA = 0.000001f;
        public static float[] alphaTemp;

        public static float[] lut1;     // MLX90640_GenerateLUTs()により、生成されるテーブル1
        public static float[] lut2;     // MLX90640_GenerateLUTs()により、生成されるテーブル2

        public static float[] lut;     // UpdateLUT()により、生成されるテーブル Toの計算に使用
        public static ushort item_num; // テーブルの要素数

        public static float table_min = -10.0f;  //　テーブルの最低温度
        public static float table_max = 100.0f;  //  テーブルの最高温度
        public static float table_step = 1.0f;   //  テーブルのstep



        public  MLX90640()
        {
            InitializeComponent();


            // 計算に使用するパラメータの生成
            mlx90640 = new mlx90640Para()
            {
                ksTo = new float[5],
                ct = new short[5],
                alpha = new ushort[768],
                offset = new short[768],
                kta = new sbyte[768],
                kv = new sbyte[768],
                cpAlpha = new float[2],
                cpOffset = new short[2],
                ilChessC = new float[3],
                brokenPixels = new ushort[5],
                outlierPixels = new ushort[5],
            };

            alphaTemp = new float[768];

        //    this.Show();

        

        //    Disp_Parameters();         // 各パラメータの表示

         //   GenerateLUTs(table_min, table_max, table_step);   // テーブルの生成

        }


        // センサ周囲温度(Ta)を得る
        // 11.2.2.3. Ambient temperature calculation (common for all pixels)
        // //
        // ・RAM
        // id   adrs  b15 b14 b13 b12 b11 b10  b9 b8 b7 b6 b5 b4 b3 b2 b1 b0
        // 768 0x700 < S ---------------  Ta_Vbe  ------------------------->
        // 800 0x720 < S ---------------  Ta_PTAT ------------------------->

        public float GetTa()
        {
            short ptat;
            float ptatArt;
            float vdd;
            float ta;


            vdd = GetVdd();     // 分解能補正と供給電圧(Vdd)


            ptat = (short)MainWindow.frameData[800];  // Ta_PTAT

            ptatArt = (float)((ptat / (ptat * mlx90640.alphaPTAT + (short)MainWindow.frameData[768])) * Math.Pow(2, 18));

            ta = (float)((ptatArt / (1 + mlx90640.KvPTAT * (vdd - 3.3)) - mlx90640.vPTAT25));

            ta = ta / mlx90640.KtPTAT + 25.0f;



            Result_Text.Text += "vdd = " + vdd.ToString() + "[V]" + "\r\n";   // vdd表示
            Result_Text.Text += "VptatArt = " + ptatArt.ToString() + "\r\n";  // VptatArt表示
            Result_Text.Text += "Ta = " + ta.ToString() + "[℃]" + "\r\n";    // ta表示



            return ta;
        }

        // 分解能補正と供給電圧(Vdd)を得る
        // 11.2.2.1. Resolution restore
        // 11.2.2.2. Supply voltage value calculation (common for all pixels)　
        //  
        // 演算式:
        //   1) resolutionCorrection = 2^(resolutionEE) / 2^(resolutionRAM)
        //         resolutionEE は、mlx90640.resolutonEE (Extract_Resolution()で入れている)
        //         resoltionRAMは、 Control register1 の b11,b10
        //         frameData[832] = Control register1 ( GetFrameData()で入れている)
        //      ADCのresolutionがデフォルト(18bit)であれば、resolutonCorrection = 1;
        //
        //   2) Vdd = (resolutionCorrection * VDDpix - Vdd25) / Kvdd + Vdd0
        //
        //
        // ・RAM
        // id   adrs  b15 b14 b13 b12 b11 b10  b9 b8 b7 b6 b5 b4 b3 b2 b1 b0
        // 810 0x72a < S ---------------  VDDpix  ------------------------->    
        //         
        // Ctrl reg1:<---------><-  -><resolut><refresh><subpage>< >< >< >< >    
        // 832 0x800d 
        //
        // ・EEPROM
        // id  adrs   b15 b14 b13 b12 b11 b10 b9 b8 b7 b6 b5 b4 b3 b2 b1 b0   
        // 51 0x2433: <S ---- ±Kv_Vdd ----------->< S ------ ±Vdd_25 ---->
        //                      

        private float GetVdd()
        {
            float vdd;
            float resolutionCorrection;

            short resolutionRAM;

            resolutionRAM = (short)((MainWindow.frameData[832] & 0x0c00) >> 10);  // 2(default) (ADC set to 18bit resolution)　　

            resolutionCorrection = (float)(Math.Pow(2, (double)mlx90640.resolutionEE) / Math.Pow(2, (double)resolutionRAM));

            vdd = (float)((resolutionCorrection * (short)MainWindow.frameData[810] - mlx90640.vdd25) / mlx90640.kVdd) + 3.3f;

            return vdd;

        }


        //  
        // 測定対象物の温度(To)の計算 
        //   Temp_objet[768]に計算結果を格納。
        //引数:
        // emissive: 放射率 0.95;	
        //  tr: reflected temperature  23.15; (adafruitの例)
        // 
        //
        public void CalculateTo(float emissivity, float tr)
        {
            float vdd;
            float ta;
            float ta4;
            float tr4;
            float taTr;
            float gain;
            float[] irDataCP = new float[2];
            float irData;
            float alphaCompensated;
            byte mode;
            sbyte ilPattern;
            sbyte chessPattern;
            sbyte pattern;
            sbyte conversionPattern;
            float Sx;
            float To;
            float[] alphaCorrR = new float[4];
            sbyte range;
            ushort subPage;
            float ktaScale;
            float kvScale;
            float alphaScale;
            float mlx_kta;
            float mlx_kv;

            subPage = MainWindow.frameData[833]; // status registerのb0 0:Sub page0 measured, 1:Sub page1 measured
       
            vdd = GetVdd();         // 分解能補正と供給電圧(Vdd)

            ta =  GetTa();          // センサ周囲温度(Ta)を得る


            ta4 = (ta + 273.15f);
            ta4 = ta4 * ta4;
            ta4 = ta4 * ta4;
            tr4 = (tr + 273.15f);       // tr=温度補正値
            tr4 = tr4 * tr4;
            tr4 = tr4 * tr4;
            taTr = tr4 - (tr4 - ta4) / emissivity;

            ktaScale = (float)Math.Pow(2, mlx90640.ktaScale);
            kvScale = (float)Math.Pow(2, mlx90640.kvScale);
            alphaScale = (float)Math.Pow(2, mlx90640.alphaScale);

            alphaCorrR[0] = 1 / (1 + mlx90640.ksTo[0] * 40);
            alphaCorrR[1] = 1;
            alphaCorrR[2] = (1 + mlx90640.ksTo[1] * mlx90640.ct[2]);
            alphaCorrR[3] = alphaCorrR[2] * (1 + mlx90640.ksTo[2] * (mlx90640.ct[3] - mlx90640.ct[2]));
      

            gain = MainWindow.frameData[778];
            
            if (gain > 32767)
            {
                gain = gain - 65536;
            }

            gain = mlx90640.gainEE / gain;

         

            mode = (byte)((MainWindow.frameData[832] & 0x1000) >> 5);   //  frameData[832]=controlRegister1(0x800d) b12=1(Chess pattern),0(Interleaved), 
                                                                        // mode=0x80= chess pattern

            if (subPage == 0)  // 直近でSubpage-0を測定し、温度を新しく計算する場合
            {
                MainWindow.to_list_0.Clear();    // 測定対象物の温度用のリストのクリア(Subpage 0用)
            }
            else
            {
                MainWindow.to_list_1.Clear();    // 測定対象物の温度用のリストのクリア(Subpage 1用)
            }

            for (short pixelNumber = 0; pixelNumber < 768; pixelNumber++)
            {
                ilPattern = (sbyte) (pixelNumber / 32 - (pixelNumber / 64) * 2);

                chessPattern = (sbyte)(ilPattern ^ (pixelNumber - (pixelNumber / 2) * 2));   // ^ XOR
       
                pattern = chessPattern;
            
                                                            // 温度は、直近に測定したSubpage(0または1)のピクセル番号の温度だけ計算する。
                if (pattern == MainWindow.frameData[833])   // frameData[833]: status register b0 : 0=Subpage0, 1=Subpage1
                {
                    irData = (short)MainWindow.frameData[pixelNumber] * gain;  // ゲイン補正 (11.2.2.5.1)

                    mlx_kta = mlx90640.kta[pixelNumber] / ktaScale;
                    mlx_kv = mlx90640.kv[pixelNumber] / kvScale;

                    irData = (float)(irData - mlx90640.offset[pixelNumber] * (1 + mlx_kta * (ta - 25)) * (1 + mlx_kv * (vdd - 3.3)));    // 11.2.2.5.3 IR Data compensation  offst,VDD and Ta
                   

                    irData = irData / emissivity;             // 11.2.2.5.4. IR data Emissivity compensation

                    float mlx_irData = irData;

                    alphaCompensated = SCALEALPHA * alphaScale / mlx90640.alpha[pixelNumber];  //SCALEALPHA = 0.000001f;
                  
                    alphaCompensated = alphaCompensated * (1 + mlx90640.KsTa * (ta - 25));

                    Sx = alphaCompensated * alphaCompensated * alphaCompensated * (irData + alphaCompensated * taTr);
                  

                    Sx = (float)(Math.Sqrt(Math.Sqrt(Sx)) * mlx90640.ksTo[1]);
                    

                    To = (float)(Math.Sqrt(Math.Sqrt(irData / (alphaCompensated * (1 - mlx90640.ksTo[1] * 273.15) + Sx) + taTr)) - 273.15f);


                    if (To < mlx90640.ct[1])                   // ct[1] = 0, To < 0℃ の場合
                    {
                        range = 0;
                    }
                    else if (To < mlx90640.ct[2])              // ct[2] = 300,  0 < To < 300の場合
                    {
                        range = 1;
                    }
                    else if (To < mlx90640.ct[3])               // ct[3] = 500 , 300 < To < 500の場合
                    {
                        range = 2;
                    }
                    else
                    {
                        range = 3;
                    }
                    // 対象物の温度(To)の計算
                    To = (float)(Math.Sqrt(Math.Sqrt(irData / (alphaCompensated * alphaCorrR[range] * (1 + mlx90640.ksTo[range] * (To - mlx90640.ct[range]))) + taTr)) - 273.15f);


                    ToData toData = new ToData();
                    toData.pixnum = (pixelNumber + 1);
                    toData.To = To;

                    if (subPage == 0)
                    {
                        MainWindow.to_list_0.Add(toData);                        // Listへ　ピクセル番号と計算した温度(To)を格納(Subpage=0用)  
                    }

                    else　
                    {
                        MainWindow.to_list_1.Add(toData);                        // Listへ　ピクセル番号と計算した温度(To)を格納(Subpage=1用)    
                    }

                }　 //  if (pattern == MainWindow.frameData[833]) 

            } // for 

        } //MLX90640_CalculateTo



        // EEPROMのデータから、温度計算に必要なパラメータの取り出し
        //
        // リターン値:
        //          =   0 : broken pixelが4つ未満、かつOutlier pixelが４つ未満
        //          =  -3 : broken pixelが4つ以上ある。
        //          =  -4 : Outlier pixelsが4つ以上ある。
        //          =  -5 : brokenとOutlier の合計が4つ以上ある。
        //          =  -6 :Broken pixel has adjacent broken pixel or
        //                 Outlier pixel has adjacent outlier pixel or 
        //                 Broken pixel has adjacent outlier pixel
        public  short Extract_Parameters()
        {
            short error = 0;

            Extract_VDD();      //  kVdd, vdd25
            Extract_PTAT();     // KvPTAT, KtPTAT, vPTAT25, alphaPTAT
            Extract_Gain();     // gainEE
            Extract_Tgc();      //  tgc
            Extract_Resolution();   // resolutionEE
            Extract_KsTa();     // KsTa
            Extract_KsTo();     // KsTo
            Extract_CP();       // cpKta,cpKv,cpAlpha[0],cpAlpha[1],cpOffset[0],cpOffset[1]
            Extract_Alpha();    // alpha[0]～alpha[767],alphaScale
            Extract_Offset();   // offset[0] ～offset[767]
            Extract_Kta();      // kta[0]～kta[767], ktaScale1
            Extract_Kv();       //  kv[0]～kv[767], kvScale
            Extract_CILCP();    //  calibrationModeEE,ilChessC[0]～ilChessC[2]

            error = Extract_DeviatingPixels();

            return error;
        }


        // 供給電圧 Vddの計算用パラメータの取り出しと、演算。(11.1.1. Restoring the VDD sensor parameters)
        //　Kvdd と vdd25を得る。
        //　演算式:
        //   Kvdd  =  Kvdd_EE * 2^(5)
        //         =  Kvdd_EE * 32
        //
        //   vdd25 = (vdd25_EE - 256 )*2^(5) - 2^(13) 
        //         = (vdd25_EE - 256 )*32 - 8192 
        //
        //　EEPROM:
        //   Kvdd_EE: EEPROM(0x2433)の b15-b8 (b15:符号bit) 
        //  vdd25_EE: EEPROM(0x2433)の b7-b1 (b7:符号bit)
        //
        // id  adrs     b15 b14 b13 b12 b11 b10 b9 b8 b7 b6 b5 b4 b3 b2 b1 b0   
        // 51 [0x2433]:< S -------- Kvdd_EE  --------><S --- vdd25_EE ------>                
        //

        private static void Extract_VDD()
        {
            short kVdd;
            short vdd25;

            kVdd = MainWindow.eeData[51];

            kVdd = (short)((MainWindow.eeData[51] & 0xff00) >> 8);

            if (kVdd > 127)
            {
                kVdd = (short)(kVdd - 256);
            }

            kVdd = (short)(kVdd * 32);

            vdd25 = (short)(MainWindow.eeData[51] & 0xff);
            vdd25 = (short)((vdd25 - 256) * 32 - 8192);

            mlx90640.kVdd = kVdd;
            mlx90640.vdd25 = vdd25;
        }


        // Ta(センサ周囲温度) 計算用パラメータの取り出しと、演算。(11.1.2. Restoring the Ta sensor parameters )
        //  KvPTAT, KtPTAT, vPTAT25, alphaPTAT を得る
        //
        // Ta = (Vptat_art/(1 + Kvptat * Delta_V) - Vptat25) / Ktptat  + 25 [℃]
        //
        //
        // 演算式:
        //   KvPTAT = KvPTAT_EE / 2^12 = KvPTAT_EE / 4096
        //   KtPTAT = KtPTAT_EE / 2^3  = KtPTAT_EE / 8
        //
        //　EEPROM:
        //  vPTAT25_EE :EEPROM(0x2431)のb15-b0  (b15:符号bit)
        //  KvPTAT_EE  :EEPROM(0x2432)のb15-b10 (b15:符号bit) 
        //  KtPTAT_EE  :EEPROM(0x2432)のb9-b0   (b9:符号bit) 
        // alphaPTAT_EE:EEPROM(0x2410)のb15-b12    
        //
        // id  adrs  b15 b14 b13 b12 b11 b10 b9 b8 b7 b6 b5 b4 b3 b2 b1 b0   
        // 16 0x2410:<-alphaPTAT_EE -><------------><-----------><--------->
        //  
        // 49 0x2431:<-S ------------ vPTAT25_EE -------------------------->
        //  
        // 50 0x2432:<-S ----- KvPTAT_EE--><-S -------------KtPTAT_EE ----->
        //


        private static void Extract_PTAT()
        {

            float KvPTAT = (MainWindow.eeData[50] & 0xfc00) >> 10;

            if (KvPTAT > 31)
            {
                KvPTAT = KvPTAT - 64;
            }

            KvPTAT = (KvPTAT / 4096);        // KvPTAT

            float KtPTAT = (MainWindow.eeData[50] & 0x3ff);

            if (KtPTAT > 511)
            {
                KtPTAT = KtPTAT - 1024;
            }

            KtPTAT = (KtPTAT / 8);            // KtPTAT

            short vPTAT25 = MainWindow.eeData[49];

            float alphaPTAT = ((MainWindow.eeData[16] & 0xf000) / 16384) + 8.0f;

            mlx90640.KvPTAT = KvPTAT;
            mlx90640.KtPTAT = KtPTAT;
            mlx90640.vPTAT25 = (ushort)vPTAT25;  　// vPTAT25は、仕様書では符号付き16bitデータ。クラスの変数の定義では、 uint16になっている。このため ushort　に変換 
            mlx90640.alphaPTAT = alphaPTAT;

        }


        // ゲインの取り出し (11.1.7. Restoring the GAIN coefficient (common for all pixels)
        //
        //　EEPROM:
        //  Gain_EE :EEPROM(0x2430)のb15-b0  (b15:符号bit)
        //
        // id  adrs  b15 b14 b13 b12 b11 b10 b9 b8 b7 b6 b5 b4 b3 b2 b1 b0   
        // 48 0x2430:<-S ------------------Gain_EE ---------------------->
        //

        private static void Extract_Gain()
        {
            short gainEE;

            gainEE = MainWindow.eeData[48];
            if (gainEE > 32767)
            {
                gainEE = (short)(gainEE - 65536);
            }

            mlx90640.gainEE = MainWindow.eeData[48];
        }


        // tgcの取り出し ( 11.1.16. Restoring the TGC coefficient)
        //
        // 演算式:
        //      tgc = tgc_EE / 2^5 = tgc_EE/32
        //　EEPROM:
        //  tgc_EE :EEPROM(0x243c)のb7-b0  (b7:符号bit)
        //
        // id adrs   b15 b14 b13 b12 b11 b10 b9 b8 b7 b6 b5 b4 b3 b2 b1 b0   
        // 60 0x243c:<-S  -------- KsTa_EE  ------><-S ------tgc_EE ------>
        //
        //  MLX90640ESF-BAx-000-TU の場合は常に 0
        // 11.1.16. Restoring the TGC coefficient)
        //  NOTE 1: In a MLX90640ESF–BAx–000-TU device, the TGC coefficient is set to 0 and must not be changed.


        private static void Extract_Tgc()
        {

            float tgc = (MainWindow.eeData[60] & 0x00ff);

            if (tgc > 127)
            {
                tgc = tgc - 256;
            }

            tgc = (float)(tgc / 32.0f);

            mlx90640.tgc = tgc;
        }



        // ResolutionEE取り出し ( 11.1.17. Restoring the resolution control coefficient)
        //
        // 演算式:
        //     resolutionEE =  ResoEE / 2^12 = ResoEE/ 4096
        //　EEPROM:
        //       ResoEE  :EEPROM(0x2438)のb13-b12 (*1)
        //
        // id  adrs   b15 b14 b13 b12 b11 b10 b9 b8 b7 b6 b5 b4 b3 b2 b1 b0   
        // 56 0x2438:<--MLX--><ResoEE><-Kv_scale->< Kta_scale1>< Kta_scale2>
        //                      

        private static void Extract_Resolution()
        {

            mlx90640.resolutionEE = (byte)((MainWindow.eeData[56] & 0x3000) >> 12);

        }


        // KsTa取り出し ( 11.2.2.8. Normalizing to sensitivity )
        //
        //　EEPROM:
        //       Ksta_EE  :EEPROM(0x243C)のb15-b8 (*1)

        // id adrs    b15 b14 b13 b12 b11 b10 b9 b8 b7 b6 b5 b4 b3 b2 b1 b0   
        // 60 0x243c:<-S  -------- KsTa_EE  ------><-S ------tgc_EE ------>
        //
        private static void Extract_KsTa()
        {

            float KsTa = (MainWindow.eeData[60] & 0xff00) >> 8;
            if (KsTa > 127)
            {
                KsTa = KsTa - 256;
            }
            KsTa = (float)(KsTa / 8192.0f);

            mlx90640.KsTa = KsTa;

        }



        // ct[],KsTo[] 取り出し (11.1.10. Restoring the KsTo coefficient (common for all pixels) )
        //
        // id adrs    b15 b14 b13 b12 b11 b10 b9 b8 b7 b6 b5 b4 b3 b2 b1 b0   
        // 61 0x243d: <---- KsTo range2 ---------><---- KsTo range1 ------->
        // 62 0x243e: <---- KsTo range4 ---------><---- KsTo range3 ------->
        // 63 0x243f: <-MLX-><-step-><----CT4 ---><-----CT3---><-KsToScale->
        //
        private static void Extract_KsTo()
        {
            sbyte step = (sbyte)(((MainWindow.eeData[63] & 0x3000) >> 12) * 10);

            mlx90640.ct[0] = -40;
            mlx90640.ct[1] = 0;
            mlx90640.ct[2] = (short)((MainWindow.eeData[63] & 0x00f0) >> 4);
            mlx90640.ct[3] = (short)((MainWindow.eeData[63] & 0X0f00) >> 8);

            mlx90640.ct[2] = (short)(mlx90640.ct[2] * step);
            mlx90640.ct[3] = (short)(mlx90640.ct[2] + mlx90640.ct[3] * step);
            mlx90640.ct[4] = 400;

            short KsToScale_EE = (short)((MainWindow.eeData[63] & 0x000f) + 8);  // eeData[63]=0x2afb, b=13, 13+8=19

            Int32 KsToScale = 1 << KsToScale_EE;                       //    KsToScale=0x00080000

            short KsToScale_short = (short)(1 << KsToScale_EE);

            mlx90640.ksTo[0] = MainWindow.eeData[61] & 0x00ff;
            mlx90640.ksTo[1] = (MainWindow.eeData[61] & 0xff00) >> 8;
            mlx90640.ksTo[2] = MainWindow.eeData[62] & 0x00ff;
            mlx90640.ksTo[3] = (MainWindow.eeData[62] & 0xff00) >> 8;

            for (int i = 0; i < 4; i++)
            {
                if (mlx90640.ksTo[i] > 127)
                {
                    mlx90640.ksTo[i] = mlx90640.ksTo[i] - 256;
                }
                mlx90640.ksTo[i] = mlx90640.ksTo[i] / KsToScale;
            }

            mlx90640.ksTo[4] = (float)(-0.0002);

        }

        //
        //  各ピクセル毎の α値の取り出し (演算含む)
        //
        // id adrs    b15 b14 b13 b12 b11 b10 b9 b8  b7 b6  b5  b4  b3  b2  b1    b0   
        // 32 0x2420:<-Alpha scale -><Scale ACC row><Scale ACC col><Scale ACC remnand>
        // 33 0x2421:<----------------  Pix sensitivity average -------------------->
        // 34 0x2422:<± Acc row 4  >< ±Acc row 3  ><± Acc row 2  >< ±Acc row 1  >
        // 35 0x2423:<± Acc row 8  >< ±Acc row 7  ><± Acc row 6  >< ±Acc row 5  >
        // 36 0x2424:<± Acc row 12 >< ±Acc row 11 ><± Acc row 10 >< ±Acc row 9  >
        // 37 0x2425:<± Acc row 16 >< ±Acc row 15 ><± Acc row 14 >< ±Acc row 13  >
        // 38 0x2426:<± Acc row 20 >< ±Acc row 19 ><± Acc row 18 >< ±Acc row 17 >
        // 39 0x2427:<± Acc row 24 >< ±Acc row 23 ><± Acc row 22 >< ±Acc row 21  >
        // 40 0x2428:<± Acc col 4  >< ±Acc col 3  ><± Acc col 2  >< ±Acc col 1  >
        // 41 0x2429:<± Acc col 8  >< ±Acc col 7  ><± Acc col 6  >< ±Acc col 5  >
        // 42 0x242A:<± Acc col 12 >< ±Acc col 11 ><± Acc col 10 >< ±Acc col 9  >
        // 43 0x242B:<± Acc col 16 >< ±Acc col 15 ><± Acc col 14 >< ±Acc col 13  >
        // 44 0x242C:<± Acc col 20 >< ±Acc col 19 ><± Acc col 18 >< ±Acc col 17 >
        // 45 0x242D:<± Acc col 24 >< ±Acc col 23 ><± Acc col 22 >< ±Acc col 21  >
        // 46 0x242E:<± Acc col 28 >< ±Acc col 27 ><± Acc col 26 >< ±Acc col 25 >
        // 47 0x242F:<± Acc col 32 >< ±Acc col 31 ><± Acc col 30 >< ±Acc col 29  >
        //
        //
        // ・各ピクセルのα値
        // id adrs    b15 b14 b13 b12 b11 b10 b9 b8 b7 b6 b5 b4 b3  b2  b1    b0   
        // 64 0x2440: <-±Offset pixel(1,1) -><-α pixel(1,1)-><±Kta(1,1)><Outlier>
        // 65 0x2441: <-±Offset pixel(1,2) -><-α pixel(1,2)-><±Kta(1,2)><Outlier>
        //                          :
        //831 0x273F:<-±Offset pixel(24,32)-><α pixel(24,32)-><±Kta(24,42)><Outlier>
        //
        private static void Extract_Alpha()
        {

            byte accRemScale = (byte)(MainWindow.eeData[32] & 0x000f);
            byte accColumnScale = (byte)((MainWindow.eeData[32] & 0x00f0) >> 4);
            byte accRowScale = (byte)((MainWindow.eeData[32] & 0x0f00) >> 8);
            byte alphaScale = (byte)(((MainWindow.eeData[32] & 0xf000) >> 12) + 30);
            short alphaRef = MainWindow.eeData[33];

            short p;

            short[] accRow = new short[24];

            for (int i = 0; i < 6; i++)
            {
                p = (short)(i * 4);
                accRow[p + 0] = (short)(MainWindow.eeData[34 + i] & 0x000f);
                accRow[p + 1] = (short)((MainWindow.eeData[34 + i] & 0x00f0) >> 4);
                accRow[p + 2] = (short)((MainWindow.eeData[34 + i] & 0x0f00) >> 8);
                accRow[p + 3] = (short)((MainWindow.eeData[34 + i] & 0xf000) >> 12);
            }

            for (int i = 0; i < 24; i++)
            {
                if (accRow[i] > 7)
                {
                    accRow[i] = (short)(accRow[i] - 16);
                }
            }

            short[] accColumn = new short[32];

            for (int i = 0; i < 8; i++)
            {
                p = (short)(i * 4);
                accColumn[p + 0] = (short)(MainWindow.eeData[40 + i] & 0x000f);
                accColumn[p + 1] = (short)((MainWindow.eeData[40 + i] & 0x00f0) >> 4);
                accColumn[p + 2] = (short)((MainWindow.eeData[40 + i] & 0x0f00) >> 8);
                accColumn[p + 3] = (short)((MainWindow.eeData[40 + i] & 0xf000) >> 12);
            }

            for (int i = 0; i < 32; i++)
            {
                if (accColumn[i] > 7)
                {
                    accColumn[i] = (short)(accColumn[i] - 16);
                }
            }





            for (int i = 0; i < 24; i++)
            {
                for (int j = 0; j < 32; j++)
                {
                    p = (short)(32 * i + j);

                    alphaTemp[p] = (MainWindow.eeData[64 + p] & 0x03f0) >> 4;

                    if (alphaTemp[p] > 31)
                    {
                        alphaTemp[p] = alphaTemp[p] - 64;
                    }
                    alphaTemp[p] = alphaTemp[p] * (1 << accRemScale);
                    alphaTemp[p] = (alphaRef + (accRow[i] << accRowScale) + (accColumn[j] << accColumnScale) + alphaTemp[p]);

                    double temp_double = Math.Pow(2, (double)alphaScale);
                    float temp_float = (float)Math.Pow(2, (float)alphaScale);

                    alphaTemp[p] = (float)(alphaTemp[p] / Math.Pow(2, (double)alphaScale));     // 2のalphaSca
                    alphaTemp[p] = alphaTemp[p] - mlx90640.tgc * (mlx90640.cpAlpha[0] + mlx90640.cpAlpha[1]) / 2;
                    alphaTemp[p] = SCALEALPHA / alphaTemp[p];
                }
            }

            float temp = alphaTemp[0];
            for (int i = 0; i < 768; i++)
            {
                if (alphaTemp[i] > temp)
                {
                    temp = alphaTemp[i];
                }
            }

            alphaScale = 0;
            while (temp < 32768)      // Adafruit=32768, Melexis=32757.4 
            {
                temp = temp * 2;
                alphaScale = (byte)(alphaScale + 1);
            }

            for (int i = 0; i < 768; i++)
            {
                temp = (float)(alphaTemp[i] * Math.Pow(2, alphaScale));

                mlx90640.alpha[i] = (ushort)(temp + 0.5);
            }

            mlx90640.alphaScale = alphaScale;

        }



        //  
        //  各ピクセル毎の offset値の取り出し (演算含む)
        // 11.2.2.5.2 Offset calculation   
        //
        // id adrs    b15 b14 b13 b12 b11 b10 b9 b8  b7 b6  b5  b4  b3  b2  b1    b0   
        // 16 0x2410:<-Alpha PTAT  -><Scale Occ row><Scale Occ col><Scale Occ remnand>
        // 17 0x2411:<---------------- ± Pix os average  -------------------------->
        // 18 0x2412:<± Occ row 4  >< ±Occ row 3  ><± Occ row 2  >< ±Occ row 1  >
        // 19 0x2413:<± Occ row 8  >< ±Occ row 7  ><± Occ row 6  >< ±Occ row 5  >
        // 20 0x2414:<± Occ row 12 >< ±Occ row 11 ><± Occ row 10 >< ±Occ row 9  >
        // 21 0x2415:<± Occ row 16 >< ±Occ row 15 ><± Occ row 14 >< ±Occ row 13  >
        // 22 0x2416:<± Occ row 20 >< ±Occ row 19 ><± Occ row 18 >< ±Occ row 17 >
        // 23 0x2417:<± Occ row 24 >< ±Occ row 23 ><± Occ row 22 >< ±Occ row 21  >
        // 24 0x2418:<± Occ col 4  >< ±Occ col 3  ><± Occ col 2  >< ±Occ col 1  >
        // 25 0x2419:<± Occ col 8  >< ±Occ col 7  ><± Occ col 6  >< ±Occ col 5  >
        // 26 0x241A:<± Occ col 12 >< ±Occ col 11 ><± Occ col 10 >< ±Occ col 9  >
        // 27 0x241B:<± Occ col 16 >< ±Occ col 15 ><± Occ col 14 >< ±Occ col 13  >
        // 28 0x241C:<± Occ col 20 >< ±Occ col 19 ><± Occ col 18 >< ±Occ col 17 >
        // 29 0x241D:<± Occ col 24 >< ±Occ col 23 ><± Occ col 22 >< ±Occ col 21  >
        // 30 0x241E:<± Occ col 28 >< ±Occ col 27 ><± Occ col 26 >< ±Occ col 25 >
        // 31 0x241F:<± Occ col 32 >< ±Occ col 31 ><± Occ col 30 >< ±Occ col 29  >
        ////
        // ・各ピクセルの Offset値
        // id adrs    b15 b14 b13 b12 b11 b10 b9 b8 b7 b6 b5 b4 b3  b2  b1    b0   
        // 64 0x2440: <-±Offset pixel(1,1) -><-α pixel(1,1)-><±Kta(1,1)><Outlier>
        // 65 0x2441: <-±Offset pixel(1,2) -><-α pixel(1,2)-><±Kta(1,2)><Outlier>
        //                          :
        //831 0x273F:<-±Offset pixel(24,32)-><α pixel(24,32)-><±Kta(24,42)><Outlier>
        //
        private static void Extract_Offset()
        {
            byte occRemScale = (byte)(MainWindow.eeData[16] & 0x000f);
            byte occColumnScale = (byte)((MainWindow.eeData[16] & 0x00f0) >> 4);
            byte occRowScale = (byte)((MainWindow.eeData[16] & 0x0f00) >> 8);

            short offsetRef = MainWindow.eeData[17];

            // short（符号付き16bitなので不要
            if (offsetRef > 32767)
            {
                offsetRef = (short)(offsetRef - 65536);
            }


            short p;
            short[] occRow = new short[24];
                                                    // Occ rowを得る
            for (int i = 0; i < 6; i++)
            {
                p = (short)(i * 4);
                occRow[p + 0] = (short)(MainWindow.eeData[18 + i] & 0x000f);
                occRow[p + 1] = (short)((MainWindow.eeData[18 + i] & 0x00f0) >> 4);
                occRow[p + 2] = (short)((MainWindow.eeData[18 + i] & 0x0f00) >> 8);
                occRow[p + 3] = (short)((MainWindow.eeData[18 + i] & 0xf000) >> 12);
            }

            for (int i = 0; i < 24; i++)
            {
                if (occRow[i] > 7)
                {
                    occRow[i] = (short)(occRow[i] - 16);
                }
            }

            short[] occColumn = new short[32];
                                                // Occ colを得る
            for (int i = 0; i < 8; i++)
            {
                p = (short)(i * 4);
                occColumn[p + 0] = (short)(MainWindow.eeData[24 + i] & 0x000f);
                occColumn[p + 1] = (short)((MainWindow.eeData[24 + i] & 0x00f0) >> 4);
                occColumn[p + 2] = (short)((MainWindow.eeData[24 + i] & 0x0f00) >> 8);
                occColumn[p + 3] = (short)((MainWindow.eeData[24 + i] & 0xf000) >> 12);
            }

            for (int i = 0; i < 32; i++)
            {
                if (occColumn[i] > 7)
                {
                    occColumn[i] = (short)(occColumn[i] - 16);
                }
            }

            for (int i = 0; i < 24; i++)
            {
                for (int j = 0; j < 32; j++)
                {
                    p = (short)(32 * i + j);
                    mlx90640.offset[p] = (short)((MainWindow.eeData[64 + p] & 0xfc00) >> 10);
                    if (mlx90640.offset[p] > 31)
                    {
                        mlx90640.offset[p] = (short)(mlx90640.offset[p] - 64);
                    }
                    mlx90640.offset[p] = (short)(mlx90640.offset[p] * (1 << occRemScale));
                    mlx90640.offset[p] = (short)(offsetRef + (occRow[i] << occRowScale) + (occColumn[j] << occColumnScale) + mlx90640.offset[p]);

                }
            }
        }


        //
        //  各ピクセル毎の Kta値の取り出し (演算含む)
        //
        // id adrs    b15 b14 b13 b12 b11 b10 b9 b8  b7 b6  b5  b4  b3  b2  b1   b0   
        // 54 0x2436:<-- ±Kta_avg_RowOdd-ColOdd --><-- ±Kta_avg_RowEven-ColOdd  ->
        // 55 0x2437:<-- ±Kta_avg_RowOdd-ColEven -><-- ±Kta_avg_RowEven-ColEven ->                    
        // 56 0x2438:< MLX  ><Res cali>< Kv scale  >< Kta_scale_1 >< Kta_scale_2   >
        //  
        // ・各ピクセルの Kta値
        // id adrs    b15 b14 b13 b12 b11 b10 b9 b8 b7 b6 b5 b4 b3  b2  b1    b0   
        // 64 0x2440: <-±Offset pixel(1,1) -><-α pixel(1,1)-><±Kta(1,1)><Outlier>
        // 65 0x2441: <-±Offset pixel(1,2) -><-α pixel(1,2)-><±Kta(1,2)><Outlier>
        //                          :
        //831 0x273F:<-±Offset pixel(24,32)-><α pixel(24,32)-><±Kta(24,42)><Outlier>
        private static void Extract_Kta()
        {
            short p;
            byte split;

            sbyte[] KtaRC = new sbyte[4];

            sbyte KtaRoCo;
            sbyte KtaRoCe;
            sbyte KtaReCo;
            sbyte KtaReCe;


            float[] ktaTemp = new float[768];
            float temp;

            KtaRoCo = (sbyte)((MainWindow.eeData[54] & 0xff00) >> 8);
            if (KtaRoCo > 127)
            {
                KtaRoCo = (sbyte)(KtaRoCo - 256);
            }
            KtaRC[0] = KtaRoCo;

            KtaReCo = (sbyte)(MainWindow.eeData[54] & 0x00ff);
            if (KtaReCo > 127)
            {
                KtaReCo = (sbyte)(KtaReCo - 256);
            }
            KtaRC[2] = KtaReCo;


            KtaRoCe = (sbyte)((MainWindow.eeData[55] & 0xff00) >> 8);
            if (KtaRoCe > 127)
            {
                KtaRoCe = (sbyte)(KtaRoCe - 256);
            }
            KtaRC[1] = KtaRoCe;


            KtaReCe = (sbyte)(MainWindow.eeData[55] & 0x00ff);
            if (KtaReCe > 127)
            {
                KtaReCe = (sbyte)(KtaReCe - 256);
            }
            KtaRC[3] = KtaReCe;



            byte ktaScale1 = (byte)(((MainWindow.eeData[56] & 0x00f0) >> 4) + 8);
            byte ktaScale2 = (byte)(MainWindow.eeData[56] & 0x000f);

            // このあたりの処理は、datasheetでの処理と異なる？

            for (int i = 0; i < 24; i++)
            {
                for (int j = 0; j < 32; j++)
                {
                    p = (short)(32 * i + j);
                    split = (byte)(2 * (p / 32 - (p / 64) * 2) + p % 2);
                    ktaTemp[p] = (MainWindow.eeData[64 + p] & 0x000E) >> 1;
                    if (ktaTemp[p] > 3)
                    {
                        ktaTemp[p] = ktaTemp[p] - 8;
                    }
                    ktaTemp[p] = ktaTemp[p] * (1 << ktaScale2);
                    ktaTemp[p] = KtaRC[split] + ktaTemp[p];
                    ktaTemp[p] = (float)(ktaTemp[p] / Math.Pow(2, ktaScale1));

                }
            }
            temp = Math.Abs(ktaTemp[0]);

            for (int i = 1; i < 768; i++)
            {
                if (Math.Abs(ktaTemp[i]) > temp)
                {
                    temp = Math.Abs(ktaTemp[i]);
                }
            }

            ktaScale1 = 0;
            while (temp < 64)
            {
                temp = temp * 2;
                ktaScale1 = (byte)(ktaScale1 + 1);
            }


            for (int i = 0; i < 768; i++)
            {
                temp = (float)(ktaTemp[i] * Math.Pow(2, ktaScale1));
                if (temp < 0)
                {
                    mlx90640.kta[i] = (sbyte)(temp - 0.5);
                }
                else
                {
                    mlx90640.kta[i] = (sbyte)(temp + 0.5);
                }

            }

            mlx90640.ktaScale = ktaScale1;

        }



        //
        //  各ピクセル毎の Kv値の取り出し (演算含む)
        //
        // id adrs    b15   b14  b13    b12   b11  b10    b9    b8   b7     b6    b5      b4   b3   b2   b1     b0   
        // 52 0x2434:<±Kvavg_rowOdd_colOdd><±Kvavg_rowEven_colOdd><±Kvavg-rowOdd_colEven><±Kvavg-rowEven_colEven> 
        //      :
        // 56 0x2438:<-- MLX --><-Res cali-><---- Kv scale --------><------ Kta_scale_1 ----><------ Kta_scale_2 --->
        //

        private static void Extract_Kv()
        {
            short p = 0;

            sbyte[] KvT = new sbyte[4];
            sbyte KvRoCo;
            sbyte KvRoCe;
            sbyte KvReCo;
            sbyte KvReCe;

            byte kvScale;
            byte split;

            float[] kvTemp = new float[768];
            float temp;


            KvRoCo = (sbyte)((MainWindow.eeData[52] & 0xf000) >> 12);
            if (KvRoCo > 7)
            {
                KvRoCo = (sbyte)(KvRoCo - 16);
            }
            KvT[0] = KvRoCo;

            KvReCo = (sbyte)((MainWindow.eeData[52] & 0x0f00) >> 8);
            if (KvReCo > 7)
            {
                KvReCo = (sbyte)(KvReCo - 16);
            }
            KvT[2] = KvReCo;

            KvRoCe = (sbyte)((MainWindow.eeData[52] & 0x00f0) >> 4);
            if (KvRoCe > 7)
            {
                KvRoCe = (sbyte)(KvRoCe - 16);
            }
            KvT[1] = KvRoCe;

            KvReCe = (sbyte)(MainWindow.eeData[52] & 0x000f);
            if (KvReCe > 7)
            {
                KvReCe = (sbyte)(KvReCe - 16);
            }
            KvT[3] = KvReCe;


            kvScale = (byte)((MainWindow.eeData[56] & 0x0f00) >> 8);

            // このあたりの処理は、datasheetでの処理と異なる？
            for (int i = 0; i < 24; i++)
            {
                for (int j = 0; j < 32; j++)
                {
                    p = (short)(32 * i + j);
                    split = (byte)(2 * (p / 32 - (p / 64) * 2) + p % 2);
                    kvTemp[p] = KvT[split];
                    kvTemp[p] = (float)(kvTemp[p] / Math.Pow(2, kvScale));

                }
            }

            temp = Math.Abs(kvTemp[0]);
            for (int i = 1; i < 768; i++)
            {
                if (Math.Abs(kvTemp[i]) > temp)
                {
                    temp = Math.Abs(kvTemp[i]);
                }
            }

            kvScale = 0;
            while (temp < 64)
            {
                temp = temp * 2;
                kvScale = (byte)(kvScale + 1);
            }

            for (int i = 0; i < 768; i++)
            {
                temp = (float)(kvTemp[i] * Math.Pow(2, kvScale));
                if (temp < 0)
                {
                    mlx90640.kv[i] = (sbyte)(temp - 0.5);
                }
                else
                {
                    mlx90640.kv[i] = (sbyte)(temp + 0.5);
                }

            }

            mlx90640.kvScale = kvScale;

        }


        //**adafruitの LX90640_example_data.xls　の cpAlpha[0],[1] ,offset[0],[1]と同じ値
        //  CP値の取り出し (演算含む)
        //
        // id adrs    b15 b14 b13 b12 b11 b10 b9 b8  b7 b6  b5  b4  b3  b2  b1    b0   
        // 32 0x2420:<-Alpha scale -><Scale ACC row><Scale ACC col><Scale ACC remnand>
        //     :
        //     :
        // 56 0x2438:< MLX  ><Res cali><- Kv scale -><- Kta_scale_1 ><- Kta_scale_2  >  
        // 57 0x2439:<±Alpha CP subpage     ><---- Alpha CP subpage_0  ------------ >
        // 58 0x243a:<±Offset(CPsubpate1 - 0><----±Offset CP subpage_0 ------------>
        // 59 0x243b:<-------- ±Kv_CP --------------><----------- ±Kta_CP --------->  
        // 

        private static void Extract_CP()
        {
            float[] alphaSP = new float[2];
            short[] offsetSP = new short[2];
            float cpKv;
            float cpKta;
            byte alphaScale;
            byte ktaScale1;
            byte kvScale;


            alphaScale = (byte)(((MainWindow.eeData[32] & 0xf000) >> 12) + 27);

            offsetSP[0] = (short)((MainWindow.eeData[58] & 0x03ff));
            if (offsetSP[0] > 511)
            {
                offsetSP[0] = (short)(offsetSP[0] - 1024);
            }

            offsetSP[1] = (short)((MainWindow.eeData[58] & 0xfc00) >> 10);
            if (offsetSP[1] > 31)
            {
                offsetSP[1] = (short)(offsetSP[1] - 64);
            }
            offsetSP[1] = (short)(offsetSP[1] + offsetSP[0]);

            alphaSP[0] = (MainWindow.eeData[57] & 0x03ff);
            if (alphaSP[0] > 511)
            {
                alphaSP[0] = alphaSP[0] - 1024;
            }
            alphaSP[0] = (float)(alphaSP[0] / Math.Pow(2, alphaScale));

            alphaSP[1] = (MainWindow.eeData[57] & 0xfc00) >> 10;
            if (alphaSP[1] > 31)
            {
                alphaSP[1] = alphaSP[1] - 64;
            }
            alphaSP[1] = (1 + alphaSP[1] / 128) * alphaSP[0];

            cpKta = (MainWindow.eeData[59] & 0x00ff);
            if (cpKta > 127)
            {
                cpKta = cpKta - 256;
            }
            ktaScale1 = (byte)(((MainWindow.eeData[56] & 0x00f0) >> 4) + 8);
            mlx90640.cpKta = (float)(cpKta / Math.Pow(2, ktaScale1));

            cpKv = (MainWindow.eeData[59] & 0xff0) >> 8;
            if (cpKv > 127)
            {
                cpKv = cpKv - 256;
            }
            kvScale = (byte)((MainWindow.eeData[56] & 0x0f00) >> 8);

            mlx90640.cpKv = (float)(cpKv / Math.Pow(2, kvScale));

            mlx90640.cpAlpha[0] = alphaSP[0];
            mlx90640.cpAlpha[1] = alphaSP[1];
            mlx90640.cpOffset[0] = offsetSP[0];
            mlx90640.cpOffset[1] = offsetSP[1];

        }


        //

        //**adafruitの LX90640_example_data.xls　の ilChessC[0],[1],[2]と同じ値
        //  CILC値の取り出し (演算含む)
        //
        // id adrs    b15 b14 b13 b12 b11 b10 b9 b8  b7 b6  b5  b4  b3  b2  b1  b0   
        // 10 0x2410: <---             Device options                         --->
        //     :
        //     :
        // 53 0x2435: < ±IL_CHESS_C3   ->< ±IL_CHESS_C2 -><-   ±IL_CHESS_C1 ->  
        //

        private static void Extract_CILCP()
        {
            float[] ilChessC = new float[3];
            byte calibrationModeEE;

            calibrationModeEE = (byte)((MainWindow.eeData[10] & 0x0800) >> 4);
            calibrationModeEE = (byte)(calibrationModeEE ^ 0x80);

            ilChessC[0] = (MainWindow.eeData[53] & 0x003f);
            if (ilChessC[0] > 31)
            {
                ilChessC[0] = ilChessC[0] - 64;
            }
            ilChessC[0] = ilChessC[0] / 16.0f;

            ilChessC[1] = (MainWindow.eeData[53] & 0x07c0) >> 6;
            if (ilChessC[1] > 15)
            {
                ilChessC[1] = ilChessC[1] - 32;
            }
            ilChessC[1] = ilChessC[1] / 2.0f;

            ilChessC[2] = (MainWindow.eeData[53] & 0xf800) >> 11;
            if (ilChessC[2] > 15)
            {
                ilChessC[2] = ilChessC[2] - 32;
            }
            ilChessC[2] = ilChessC[2] / 8.0f;

            mlx90640.calibrationModeEE = calibrationModeEE;

            mlx90640.ilChessC[0] = ilChessC[0];
            mlx90640.ilChessC[1] = ilChessC[1];
            mlx90640.ilChessC[2] = ilChessC[2];

        }


        //
        //  不良ピクセル(broken , outlier)
        //
        // リターン値:
        //          =   0 : broken pixelが4つ未満、かつOutlier pixelが４つ未満
        //          =  -3 : broken pixelが4つ以上ある。
        //          =  -4 : Outlier pixelsが4つ以上ある。
        //          =  -5 : brokenとOutlier の合計が4つ以上ある。
        //          =  -6 :Broken pixel has adjacent broken pixel or
        //                 Outlier pixel has adjacent outlier pixel or 
        //                 Broken pixel has adjacent outlier pixel
        //
        private static short Extract_DeviatingPixels()
        {
            ushort pixCnt = 0;
            ushort brokenPixCnt = 0;
            ushort outlierPixCnt = 0;
            short warn = 0;
            short i;

            for (pixCnt = 0; pixCnt < 5; pixCnt++)
            {
                mlx90640.brokenPixels[pixCnt] = 0xFFFF;
                mlx90640.outlierPixels[pixCnt] = 0xFFFF;
            }

            pixCnt = 0;
            while (pixCnt < 768 && brokenPixCnt < 5 && outlierPixCnt < 5)
            {
                if (MainWindow.eeData[pixCnt + 64] == 0)
                {
                    mlx90640.brokenPixels[brokenPixCnt] = pixCnt;
                    brokenPixCnt = (ushort)(brokenPixCnt + 1);
                }
                else if ((MainWindow.eeData[pixCnt + 64] & 0x0001) != 0)
                {
                    mlx90640.outlierPixels[outlierPixCnt] = pixCnt;
                    outlierPixCnt = (ushort)(outlierPixCnt + 1);
                }

                pixCnt = (ushort)(pixCnt + 1);

            }

            if (brokenPixCnt > 4)   // Broken pixels: brokenPixCnt
            {
                warn = -3;
            }
            else if (outlierPixCnt > 4) // Outlier pixels: outlierPixCnt
            {
                warn = -4;
            }
            else if ((brokenPixCnt + outlierPixCnt) > 4)  // Broken+outlier pixels:brokenPixCnt + outlierPixCnt
            {
                warn = -5;
            }
            else
            {
                for (pixCnt = 0; pixCnt < brokenPixCnt; pixCnt++)
                {
                    for (i = (short)(pixCnt + 1); i < brokenPixCnt; i++)
                    {
                        warn = CheckAdjacentPixels(mlx90640.brokenPixels[pixCnt], mlx90640.brokenPixels[i]);
                        if (warn != 0)      // Broken pixel has adjacent broken pixel"
                        {

                            return warn;
                        }
                    }
                }

                for (pixCnt = 0; pixCnt < outlierPixCnt; pixCnt++)
                {
                    for (i = (short)(pixCnt + 1); i < outlierPixCnt; i++)
                    {
                        warn = CheckAdjacentPixels(mlx90640.outlierPixels[pixCnt], mlx90640.outlierPixels[i]);

                        if (warn != 0)          // Outlier pixel has adjacent outlier pixel
                        {

                            return warn;
                        }
                    }
                }

                for (pixCnt = 0; pixCnt < brokenPixCnt; pixCnt++)   // Broken pixel has adjacent outlier pixel
                {
                    for (i = 0; i < outlierPixCnt; i++)
                    {
                        warn = CheckAdjacentPixels(mlx90640.brokenPixels[pixCnt], mlx90640.outlierPixels[i]);
                        if (warn != 0)
                        {
                            return warn;
                        }
                    }
                }

            }


            return warn;


        }


        // Adjacent:隣接する
        // (Extract_DeviatingPixels)から呼ばれるルーチン
        //
        private static short CheckAdjacentPixels(ushort pix1, ushort pix2)
        {
            ushort lp1 = (ushort)(pix1 >> 5);
            ushort lp2 = (ushort)(pix2 >> 5);
            ushort cp1 = (ushort)(pix1 - (lp1 << 5));
            ushort cp2 = (ushort)(pix2 - (lp2 << 5));

            short pixPosDif = (short)(lp1 - lp2);
            if (pixPosDif > -2 && pixPosDif < 2)
            {
                pixPosDif = (short)(cp1 - cp2);
                if (pixPosDif > -2 && pixPosDif < 2)
                {
                    return -6;
                }

            }

            return 0;

        }



        //  pixelの異常判定
        //  入力したpixelが　brokenまたはoutlier の場合、1を返す。　
        private static int IsPixelBad(ushort pixel)
        {
            for (short i = 0; i < 5; i++)
            {
                if (pixel == mlx90640.outlierPixels[i] || pixel == mlx90640.brokenPixels[i])
                {
                    return 1;
                }
            }

            return 0;
        }



        // 各パラメータの表示
        private void Disp_Parameters()
        {
            Para_Text.Text = "kVdd =" + " " + mlx90640.kVdd.ToString() + "\r\n";

            Para_Text.Text += "vdd25 =" + " " + mlx90640.vdd25.ToString() + "\r\n";

            Para_Text.Text += "KvPTAT =" + " " + mlx90640.KvPTAT.ToString() + "\r\n";

            Para_Text.Text += "KtPTAT =" + " " + mlx90640.KtPTAT.ToString() + "\r\n";

            Para_Text.Text += "vPTAT25 =" + " " + mlx90640.vPTAT25.ToString() + "\r\n";

            Para_Text.Text += "alphaPTAT =" + " " + mlx90640.alphaPTAT.ToString() + "\r\n";

            Para_Text.Text += "gainEE =" + " " + mlx90640.gainEE.ToString() + "\r\n";

            Para_Text.Text += "tgc =" + " " + mlx90640.tgc.ToString() + "\r\n";

            Para_Text.Text += "cpKv =" + " " + mlx90640.cpKv.ToString() + "\r\n";

            Para_Text.Text += "cpKta =" + " " + mlx90640.cpKta.ToString() + "\r\n";

            Para_Text.Text += "resolutionEE =" + " " + mlx90640.resolutionEE.ToString() + "\r\n";

            Para_Text.Text += "calibrationModeEE =" + " " + mlx90640.calibrationModeEE.ToString() + "\r\n";

            Para_Text.Text += "KsTa =" + " " + mlx90640.KsTa.ToString() + "\r\n";

            Para_Text.Text += "\r\n";

            for (int i = 0; i < 5; i++)
            {
                Para_Text.Text += "KsTo[" + i.ToString() + "]=" + " " + mlx90640.ksTo[i].ToString() + "\r\n";
            }
            Para_Text.Text += "\r\n";


            for (int i = 0; i < 5; i++)
            {
                Para_Text.Text += "ct[" + i.ToString() + "]=" + " " + mlx90640.ct[i].ToString() + "\r\n";
            }
            Para_Text.Text += "\r\n";


            for (int i = 0; i < 768; i++)
            {
                Para_Text.Text += "alpha[" + i.ToString() + "]=" + " " + mlx90640.alpha[i].ToString() + "\r\n";
            }
            Para_Text.Text += "\r\n";

            Para_Text.Text += "alphaScale =" + mlx90640.alphaScale.ToString() + "\r\n";
            Para_Text.Text += "\r\n";

            for (int i = 0; i < 768; i++)
            {
                Para_Text.Text += "offset[" + i.ToString() + "]=" + " " + mlx90640.offset[i].ToString() + "\r\n";
            }
            Para_Text.Text += "\r\n";


            for (int i = 0; i < 768; i++)
            {
                Para_Text.Text += "kta[" + i.ToString() + "]=" + " " + mlx90640.kta[i].ToString() + "\r\n";
            }
            Para_Text.Text += "\r\n";

            Para_Text.Text += "ktaScale =" + mlx90640.ktaScale.ToString() + "\r\n";
            Para_Text.Text += "\r\n";

            for (int i = 0; i < 768; i++)
            {
                Para_Text.Text += "kv[" + i.ToString() + "]=" + " " + mlx90640.kv[i].ToString() + "\r\n";
            }
            Para_Text.Text += "\r\n";

            Para_Text.Text += "kvScale =" + mlx90640.kvScale.ToString() + "\r\n";
            Para_Text.Text += "\r\n";


            for (int i = 0; i < 2; i++)
            {
                Para_Text.Text += "cpAlpha[" + i.ToString() + "]=" + " " + mlx90640.cpAlpha[i].ToString("G9") + "\r\n";
            }
            for (int i = 0; i < 2; i++)
            {
                Para_Text.Text += "cpOffset[" + i.ToString() + "]=" + " " + mlx90640.cpOffset[i].ToString() + "\r\n";
            }
            Para_Text.Text += "\r\n";


            for (int i = 0; i < 3; i++)
            {
                Para_Text.Text += "ilChessC[" + i.ToString() + "]=" + " " + mlx90640.ilChessC[i].ToString() + "\r\n";
            }
            Para_Text.Text += "\r\n";


            for (int i = 0; i < 5; i++)
            {
                Para_Text.Text += "brokenPixels[" + i.ToString() + "]=" + " " + mlx90640.brokenPixels[i].ToString() + "\r\n";
            }
            for (int i = 0; i < 5; i++)
            {
                Para_Text.Text += "outlierPixels[" + i.ToString() + "]=" + " " + mlx90640.outlierPixels[i].ToString() + "\r\n";
            }
        }




    }
}
