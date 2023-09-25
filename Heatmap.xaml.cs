using ScottPlot;
using ScottPlot.Drawing;
using ScottPlot.Plottable;
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
    /// <summary>
    /// Heatmap.xaml の相互作用ロジック
    /// </summary>
    public partial class Heatmap : Window
    {
        public Heatmap()
        {
            InitializeComponent();
           
            wpfPlot_Heatmap.Configuration.Pan = false;               // パン(グラフの移動)不可

            wpfPlot_Heatmap.Configuration.ScrollWheelZoom = false;   //ズーム(グラフの拡大、縮小)不可

        }


        //
        // ヒートマップの軸　表示処理
        //
        public void Disp_XY_Axis()
        {

            wpfPlot_Heatmap.Plot.XAxis.Ticks(false, false, true); // X軸の大きい目盛り=非表示, X軸の小さい目盛り=非表示, X軸の目盛りのラベル=表示
            wpfPlot_Heatmap.Plot.XAxis.TickLabelStyle(fontSize: 16);

            double[] x_positions = new double[32];              // マニュアルラベルの作成(X軸目盛り)
            string[] x_labels = new string[32];

            for (int i = 0; i < x_positions.Length; i++)
            {
                x_positions[i] = i + 0.5;
                x_labels[i] = (31 - i).ToString();
            }
            wpfPlot_Heatmap.Plot.XAxis.ManualTickPositions(x_positions, x_labels);  // マニュアルラベルの表示(X軸の目盛り)

            wpfPlot_Heatmap.Plot.XAxis.Label("Column");     // X軸のラベル


            wpfPlot_Heatmap.Plot.YAxis.Ticks(false, false, true); // Y軸の大きい目盛り=非表示, Y軸の小さい目盛り=非表示, Y軸の目盛りのラベル=表示
            wpfPlot_Heatmap.Plot.YAxis.TickLabelStyle(fontSize: 16);

            double[] y_positions = new double[24];              // マニュアルラベルの作成(Y軸目盛り)
            string[] y_labels = new string[24];

            for (int i = 0; i < y_positions.Length; i++)
            {
                y_positions[i] = i + 0.5;
                y_labels[i] = "Row " + (23 - i).ToString();
            }
            wpfPlot_Heatmap.Plot.YAxis.ManualTickPositions(y_positions, y_labels);  // マニュアルラベルの表示(Y軸の目盛り)

            wpfPlot_Heatmap.Plot.Margins(0, 0); 

        }

        // Heatmapの表示
        //
        // ヒートマップ:https://scottplot.net/cookbook/4.1/#heatmap
        // カラーバー:  https://scottplot.net/cookbook/4.1/category/plottable-colorbar/
        // カラーマップ:  https://scottplot.net/cookbook/4.1/colormaps/
        //
        public void Disp_Heatmap()
        {
            double[,] pixelTo;
            pixelTo = new double[24, 32];

            for (Byte i = 0; i < 24; i++)            // row
            {
                for (Byte j = 0; j < 32; j++)        // col
                {
                    int pix_num = i * 32 + j + 1;         // ピクセル番号

                    ToData toData = MainWindow.to_list.First(t => t.pixnum == pix_num); //  pix_numと一致する、pixDataオブジェクトを得る

                    pixelTo[i, j] = toData.To;          //  ピクセル番号の対象物の温度(To)

                }
            }

                                                    // カラーバーの種類を選択
            Colormap colormap = ScottPlot.Drawing.Colormap.Turbo;

            if (MainWindow.colorbar_type == 0 )  // turbo 
            {
                colormap = ScottPlot.Drawing.Colormap.Turbo; 
            }
            else if (MainWindow.colorbar_type == 1 )    // blues
            {
                colormap = ScottPlot.Drawing.Colormap.Blues;
            }
            else if ( MainWindow.colorbar_type == 2 )   // grayscale
            {
                colormap = ScottPlot.Drawing.Colormap.Grayscale;
            }


            var hm = wpfPlot_Heatmap.Plot.AddHeatmap(pixelTo, colormap, lockScales: false); // ヒートマップ

            hm.Smooth = true;                              // バイキュービック補間
            hm.FlipVertically = false;                     // 最初の行がヒートマップの上側
            hm.FlipHorizontally = true;                    // 最初のカラムがヒートマップの左側

            var cb = wpfPlot_Heatmap.Plot.AddColorbar(hm,space:0); // カラーバー表示
           
            cb.AutomaticTicks(false);     // カラーバー目盛りの自動表示 false

            wpfPlot_Heatmap.Refresh();          // 表示


            // 最大値と最小値を求める
            float t_min = MainWindow.to_list.Where(t => ((t.pixnum >= 1) && (t.pixnum <= 768))).Min(t => t.To);       //  to_list ピクセル番号 ( 1 ～ 768 )内の最低温度 
            float t_max = MainWindow.to_list.Where(t => ((t.pixnum >= 1) && (t.pixnum <= 768))).Max(t => t.To);       //  to_list ピクセル番号 ( 1 ～ 768 )内の最高温度

            float t_mid = (float)((t_max - t_min) / 2.0) + t_min;

            TB_max_val.Text = t_max.ToString("F0") + "  [℃]";  // 最大値
            TB_mid_val.Text = t_mid.ToString("F0");  // 中央値
            TB_min_val.Text = t_min.ToString("F0");  // 最小値

        }


        // Windowが閉じられる際の処理
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            MainWindow.heatmap_window_cnt = 0;     // ヒートマップウィンドウの表示個数のクリア

        }

    }
}
