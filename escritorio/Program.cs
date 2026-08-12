// Contador de puntos tactiles - app de escritorio WPF.
//
// Misma idea que trazos.html: una ventana, dibujas con los dedos, arriba el
// numero de puntos activos y cada trazo con su color. El suavizado es el mismo
// (la punta persigue al dedo con easing por fotograma), aqui sobre WPF.
//
// Escrito a proposito con sintaxis de C# 5 y sin XAML, para que compile tanto
// con el SDK de .NET moderno como con el csc.exe que ya trae Windows.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Trazos
{
    /* ---------------- datos ---------------- */

    internal struct Punto
    {
        public double X;
        public double Y;
        public double W;

        public Punto(double x, double y, double w)
        {
            X = x;
            Y = y;
            W = w;
        }
    }

    internal class Trazo
    {
        public readonly List<Punto> Puntos = new List<Punto>();
        public Brush Pincel;

        public double Ex;              // punta suavizada: lo que se dibuja
        public double Ey;
        public double Ew;              // grosor suavizado
        public double Dx;              // destino real del dedo
        public double Dy;
        public double Vel;             // px por milisegundo, suavizada
        public double UltimoMovimiento;

        public ContainerVisual Raiz;   // el trazo entero
        public DrawingVisual Punta;    // solo el ultimo tramo, se repinta cada fotograma
    }

    /* ---------------- lienzo ---------------- */

    // Modo retenido: cada tramo ya cerrado es un visual propio que WPF conserva.
    // Asi el bucle no vuelve a tocar lo ya dibujado por muy largo que sea el trazo.
    internal class Lienzo : FrameworkElement
    {
        private readonly VisualCollection hijos;
        private readonly Brush fondo;

        public Lienzo(Brush fondo)
        {
            this.fondo = fondo;
            hijos = new VisualCollection(this);
            SizeChanged += delegate { InvalidateVisual(); };
        }

        protected override int VisualChildrenCount
        {
            get { return hijos.Count; }
        }

        protected override Visual GetVisualChild(int indice)
        {
            return hijos[indice];
        }

        // Pintar el fondo aqui, y no en un panel de detras, es lo que hace que
        // toda la superficie del elemento reciba los eventos tactiles.
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(fondo, null, new Rect(new Point(0, 0), RenderSize));
        }

        public void Agregar(Visual v)
        {
            hijos.Add(v);
        }

        public void Quitar(Visual v)
        {
            hijos.Remove(v);
        }

        public void Vaciar()
        {
            hijos.Clear();
        }
    }

    /* ---------------- ventana ---------------- */

    internal class Ventana : Window
    {
        // Cuanto persigue la punta al dedo en un fotograma de 60 Hz: mas bajo es
        // mas suave pero con mas retardo. 0.35 es donde deja de verse dentado.
        private const double SEGUIMIENTO = 0.35;
        private const double SEGUIMIENTO_GROSOR = 0.18;
        private const double GROSOR_BASE = 5.0;
        private const double GROSOR_MIN = 1.6;
        private const double VEL_MAX = 3.2;      // px/ms a los que el trazo adelgaza del todo
        private const double DIST_MIN = 0.4;     // por debajo de esto no acumulamos punto

        private const int ID_RATON = int.MinValue;

        // El diccionario es la pieza clave del multitactil: cada dedo tiene su
        // id, su trazo, su punta suavizada y su color, sin mezclarse con los demas.
        private readonly Dictionary<int, Trazo> activos = new Dictionary<int, Trazo>();
        private readonly List<Trazo> terminados = new List<Trazo>();

        private readonly Lienzo lienzo;
        private readonly TextBlock numero;
        private readonly TextBlock pista;
        private readonly ScaleTransform escala;
        private readonly SolidColorBrush colorNumero;
        private readonly Button btnDeshacer;
        private readonly Button btnLimpiar;

        private readonly Stopwatch reloj = Stopwatch.StartNew();
        private double ultimoMs;
        private int giro;

        private bool pantallaCompleta;
        private WindowState estadoPrevio;
        private WindowStyle estiloPrevio;
        private ResizeMode modoPrevio;

        public Ventana()
        {
            Title = "Contador de puntos tactiles";
            Width = 1280;
            Height = 820;
            MinWidth = 640;
            MinHeight = 480;
            Background = Pincel("#0F1116");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");

            /* --- barra superior --- */

            colorNumero = new SolidColorBrush(Tono("#E8ECF4"));   // sin Freeze: se anima

            numero = new TextBlock();
            numero.Text = "0";
            numero.FontSize = 44;
            numero.FontWeight = FontWeights.Bold;
            numero.Foreground = colorNumero;
            numero.MinWidth = 56;
            numero.TextAlignment = TextAlignment.Right;
            numero.RenderTransformOrigin = new Point(0.5, 0.5);
            escala = new ScaleTransform(1.0, 1.0);
            numero.RenderTransform = escala;

            TextBlock etiqueta = new TextBlock();
            etiqueta.Text = "PUNTOS ACTIVOS";
            etiqueta.FontSize = 13;
            etiqueta.Foreground = Pincel("#8B93A7");
            etiqueta.VerticalAlignment = VerticalAlignment.Bottom;
            etiqueta.Margin = new Thickness(10, 0, 0, 6);

            StackPanel marcador = new StackPanel();
            marcador.Orientation = Orientation.Horizontal;
            marcador.Children.Add(numero);
            marcador.Children.Add(etiqueta);

            btnLimpiar = Boton("Limpiar");
            btnLimpiar.Click += delegate { Limpiar(); };
            btnDeshacer = Boton("Deshacer");
            btnDeshacer.Click += delegate { Deshacer(); };

            DockPanel fila = new DockPanel();
            fila.LastChildFill = false;
            DockPanel.SetDock(marcador, Dock.Left);
            DockPanel.SetDock(btnLimpiar, Dock.Right);
            DockPanel.SetDock(btnDeshacer, Dock.Right);
            fila.Children.Add(marcador);
            fila.Children.Add(btnLimpiar);
            fila.Children.Add(btnDeshacer);

            Border barra = new Border();
            barra.Background = Pincel("#171A22");
            barra.BorderBrush = Pincel("#252A35");
            barra.BorderThickness = new Thickness(0, 0, 0, 1);
            barra.Padding = new Thickness(18, 12, 18, 12);
            barra.Child = fila;

            /* --- lienzo --- */

            lienzo = new Lienzo(Pincel("#0F1116"));
            lienzo.ClipToBounds = true;

            // Sin esto, en una pantalla tactil de Windows el mantener pulsado saca
            // el circulito del clic derecho y te corta el trazo a media raya.
            Stylus.SetIsPressAndHoldEnabled(lienzo, false);
            Stylus.SetIsFlicksEnabled(lienzo, false);
            Stylus.SetIsTapFeedbackEnabled(lienzo, false);
            Stylus.SetIsTouchFeedbackEnabled(lienzo, false);

            lienzo.TouchDown += LienzoTouchDown;
            lienzo.TouchMove += LienzoTouchMove;
            lienzo.TouchUp += LienzoTouchUp;
            lienzo.LostTouchCapture += LienzoLostTouchCapture;

            lienzo.MouseLeftButtonDown += LienzoMouseDown;
            lienzo.MouseMove += LienzoMouseMove;
            lienzo.MouseLeftButtonUp += LienzoMouseUp;
            lienzo.LostMouseCapture += LienzoLostMouseCapture;

            pista = new TextBlock();
            pista.Text = "Dibuja con el dedo, el lapiz o el raton";
            pista.FontSize = 17;
            pista.Foreground = Pincel("#8B93A7");
            pista.HorizontalAlignment = HorizontalAlignment.Center;
            pista.VerticalAlignment = VerticalAlignment.Center;
            pista.IsHitTestVisible = false;

            Grid zona = new Grid();
            zona.Children.Add(lienzo);
            zona.Children.Add(pista);

            Grid raiz = new Grid();
            RowDefinition arriba = new RowDefinition();
            arriba.Height = GridLength.Auto;
            raiz.RowDefinitions.Add(arriba);
            raiz.RowDefinitions.Add(new RowDefinition());
            Grid.SetRow(barra, 0);
            Grid.SetRow(zona, 1);
            raiz.Children.Add(barra);
            raiz.Children.Add(zona);
            Content = raiz;

            KeyDown += VentanaTecla;
            Deactivated += delegate { CerrarTodos(); };
            Closed += delegate { CompositionTarget.Rendering -= Fotograma; };
            CompositionTarget.Rendering += Fotograma;

            Actualizar();
        }

        /* ---------------- entrada ---------------- */

        private void LienzoTouchDown(object remitente, TouchEventArgs e)
        {
            Empezar(e.TouchDevice.Id, e.GetTouchPoint(lienzo).Position);
            e.TouchDevice.Capture(lienzo);
            e.Handled = true;   // corta la promocion a raton: si no, un dedo haria dos trazos
        }

        private void LienzoTouchMove(object remitente, TouchEventArgs e)
        {
            // WPF agrupa las muestras que llegan entre fotogramas; recuperarlas es
            // lo que evita que un trazo rapido salga poligonal.
            TouchPointCollection medias = e.GetIntermediateTouchPoints(lienzo);
            if (medias != null && medias.Count > 0)
            {
                for (int i = 0; i < medias.Count; i++)
                {
                    Mover(e.TouchDevice.Id, medias[i].Position);
                }
            }
            else
            {
                Mover(e.TouchDevice.Id, e.GetTouchPoint(lienzo).Position);
            }
            e.Handled = true;
        }

        private void LienzoTouchUp(object remitente, TouchEventArgs e)
        {
            Terminar(e.TouchDevice.Id);
            e.Handled = true;
        }

        // Si el dedo desaparece sin avisar (rechazo de palma, un gesto del sistema,
        // la ventana pierde el foco) el trazo se quedaria vivo para siempre.
        private void LienzoLostTouchCapture(object remitente, TouchEventArgs e)
        {
            Terminar(e.TouchDevice.Id);
        }

        private void LienzoMouseDown(object remitente, MouseButtonEventArgs e)
        {
            Empezar(ID_RATON, e.GetPosition(lienzo));
            lienzo.CaptureMouse();
        }

        private void LienzoMouseMove(object remitente, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }
            Mover(ID_RATON, e.GetPosition(lienzo));
        }

        private void LienzoMouseUp(object remitente, MouseButtonEventArgs e)
        {
            Terminar(ID_RATON);
            lienzo.ReleaseMouseCapture();
        }

        private void LienzoLostMouseCapture(object remitente, MouseEventArgs e)
        {
            Terminar(ID_RATON);
        }

        private void VentanaTecla(object remitente, KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                AlternarPantallaCompleta();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && pantallaCompleta)
            {
                AlternarPantallaCompleta();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                Limpiar();
            }
            else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                Deshacer();
            }
        }

        /* ---------------- trazos ---------------- */

        private void Empezar(int id, Point p)
        {
            if (activos.ContainsKey(id))
            {
                return;
            }

            Trazo t = new Trazo();
            t.Pincel = SiguienteColor();
            t.Ex = p.X;
            t.Ey = p.Y;
            t.Dx = p.X;
            t.Dy = p.Y;
            t.Ew = GROSOR_BASE * 0.7;
            t.UltimoMovimiento = reloj.Elapsed.TotalMilliseconds;
            t.Puntos.Add(new Punto(p.X, p.Y, t.Ew));

            t.Raiz = new ContainerVisual();
            t.Punta = new DrawingVisual();
            t.Raiz.Children.Add(t.Punta);
            lienzo.Agregar(t.Raiz);

            activos[id] = t;
            RepintarPunta(t);
            Latir();
            Actualizar();
        }

        private void Mover(int id, Point p)
        {
            Trazo t;
            if (!activos.TryGetValue(id, out t))
            {
                return;
            }

            double ahora = reloj.Elapsed.TotalMilliseconds;
            double dx = p.X - t.Dx;
            double dy = p.Y - t.Dy;
            double ms = Math.Max(1.0, ahora - t.UltimoMovimiento);

            t.Vel += (Math.Sqrt(dx * dx + dy * dy) / ms - t.Vel) * 0.3;
            t.UltimoMovimiento = ahora;
            t.Dx = p.X;
            t.Dy = p.Y;
        }

        // Idempotente a proposito: el mismo camino sirve para el dedo que se
        // levanta bien y para el que desaparece sin avisar, y da igual que
        // lleguen los dos avisos.
        private void Terminar(int id)
        {
            Trazo t;
            if (!activos.TryGetValue(id, out t))
            {
                return;
            }

            activos.Remove(id);
            terminados.Add(t);
            Actualizar();
        }

        private void CerrarTodos()
        {
            List<int> ids = new List<int>(activos.Keys);
            for (int i = 0; i < ids.Count; i++)
            {
                Terminar(ids[i]);
            }
        }

        private void Deshacer()
        {
            if (terminados.Count == 0)
            {
                return;
            }
            Trazo t = terminados[terminados.Count - 1];
            terminados.RemoveAt(terminados.Count - 1);
            lienzo.Quitar(t.Raiz);
            Actualizar();
        }

        private void Limpiar()
        {
            terminados.Clear();
            activos.Clear();
            lienzo.Vaciar();
            Actualizar();
        }

        /* ---------------- bucle ---------------- */

        private void Fotograma(object remitente, EventArgs e)
        {
            double ahora = reloj.Elapsed.TotalMilliseconds;
            double dt = Math.Min(64.0, ahora - ultimoMs);
            ultimoMs = ahora;

            if (activos.Count == 0)
            {
                return;
            }

            // Factor normalizado al tiempo real del fotograma: la suavidad es la
            // misma a 60 Hz que a 165 Hz.
            double k = 1.0 - Math.Pow(1.0 - SEGUIMIENTO, dt / 16.667);
            double kg = 1.0 - Math.Pow(1.0 - SEGUIMIENTO_GROSOR, dt / 16.667);

            foreach (Trazo t in activos.Values)
            {
                t.Ex += (t.Dx - t.Ex) * k;
                t.Ey += (t.Dy - t.Ey) * k;

                // El grosor sale de la velocidad, que es lo que hace que parezca
                // tinta y no un cable de grosor constante.
                double objetivo = GROSOR_MIN + (GROSOR_BASE - GROSOR_MIN) * (1.0 - Math.Min(1.0, t.Vel / VEL_MAX));
                t.Ew += (objetivo - t.Ew) * kg;

                Punto ultimo = t.Puntos[t.Puntos.Count - 1];
                double dx = t.Ex - ultimo.X;
                double dy = t.Ey - ultimo.Y;

                if (Math.Sqrt(dx * dx + dy * dy) >= DIST_MIN)
                {
                    t.Puntos.Add(new Punto(t.Ex, t.Ey, t.Ew));
                    FijarTramo(t);
                }
                else
                {
                    t.Puntos[t.Puntos.Count - 1] = new Punto(ultimo.X, ultimo.Y, t.Ew);
                }

                RepintarPunta(t);
            }
        }

        /* ---------------- dibujo ---------------- */

        // Con tres puntos ya se puede cerrar el tramo del medio: va de la mitad del
        // par anterior a la mitad del par nuevo, curvando sobre el punto central.
        // Asi las uniones no tienen esquinas. Cada tramo se pinta una sola vez.
        private static void FijarTramo(Trazo t)
        {
            int n = t.Puntos.Count;
            if (n < 3)
            {
                return;
            }

            Punto a = t.Puntos[n - 3];
            Punto b = t.Puntos[n - 2];
            Punto c = t.Puntos[n - 1];

            Point desde = (n == 3) ? new Point(a.X, a.Y) : Mitad(a, b);
            Point hasta = Mitad(b, c);

            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                Curva(dc, t.Pincel, desde, new Point(b.X, b.Y), hasta, (a.W + c.W) / 2.0);
            }
            t.Raiz.Children.Add(dv);
        }

        // La punta es el unico pedazo que cambia de un fotograma a otro. Se solapa
        // a proposito con el ultimo tramo fijado, que es mas barato que cuadrar el
        // empalme al pixel y no se nota al ser del mismo color.
        private static void RepintarPunta(Trazo t)
        {
            using (DrawingContext dc = t.Punta.RenderOpen())
            {
                int n = t.Puntos.Count;
                Punto ultimo = t.Puntos[n - 1];

                if (n == 1)
                {
                    dc.DrawEllipse(t.Pincel, null, new Point(ultimo.X, ultimo.Y), ultimo.W / 2.0, ultimo.W / 2.0);
                    return;
                }

                Punto previo = t.Puntos[n - 2];
                Point desde = (n == 2) ? new Point(previo.X, previo.Y) : Mitad(t.Puntos[n - 3], previo);
                Curva(dc, t.Pincel, desde, new Point(previo.X, previo.Y),
                      new Point(ultimo.X, ultimo.Y), (previo.W + ultimo.W) / 2.0);
            }
        }

        private static void Curva(DrawingContext dc, Brush pincel, Point desde, Point control, Point hasta, double grosor)
        {
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(desde, false, false);
                c.QuadraticBezierTo(control, hasta, true, false);
            }
            g.Freeze();

            Pen lapiz = new Pen(pincel, Math.Max(0.6, grosor));
            lapiz.StartLineCap = PenLineCap.Round;
            lapiz.EndLineCap = PenLineCap.Round;
            lapiz.LineJoin = PenLineJoin.Round;
            lapiz.Freeze();

            dc.DrawGeometry(null, lapiz, g);
        }

        private static Point Mitad(Punto a, Punto b)
        {
            return new Point((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
        }

        /* ---------------- marcador y color ---------------- */

        private void Actualizar()
        {
            numero.Text = activos.Count.ToString();

            btnDeshacer.IsEnabled = terminados.Count > 0;
            btnLimpiar.IsEnabled = terminados.Count > 0 || activos.Count > 0;
            btnDeshacer.Opacity = btnDeshacer.IsEnabled ? 1.0 : 0.4;
            btnLimpiar.Opacity = btnLimpiar.IsEnabled ? 1.0 : 0.4;

            pista.Visibility = (terminados.Count == 0 && activos.Count == 0)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void Latir()
        {
            DoubleAnimation salto = new DoubleAnimation(1.0, 1.18, TimeSpan.FromMilliseconds(120));
            salto.AutoReverse = true;
            salto.EasingFunction = new CubicEase();
            escala.BeginAnimation(ScaleTransform.ScaleXProperty, salto);
            escala.BeginAnimation(ScaleTransform.ScaleYProperty, salto);

            ColorAnimation destello = new ColorAnimation(Tono("#4DA3FF"), TimeSpan.FromMilliseconds(120));
            destello.AutoReverse = true;
            colorNumero.BeginAnimation(SolidColorBrush.ColorProperty, destello);
        }

        // Angulo aureo: cada trazo cae lo mas lejos posible del anterior en el
        // circulo de color, asi no se parecen ni los seguidos ni los simultaneos.
        private Brush SiguienteColor()
        {
            double h = (giro * 137.508) % 360.0;
            giro++;
            SolidColorBrush b = new SolidColorBrush(DesdeHsl(h, 0.85, 0.66));
            b.Freeze();
            return b;
        }

        private static Color DesdeHsl(double h, double s, double l)
        {
            double c = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
            double x = c * (1.0 - Math.Abs(((h / 60.0) % 2.0) - 1.0));
            double m = l - c / 2.0;
            double r = 0.0;
            double g = 0.0;
            double b = 0.0;

            if (h < 60.0) { r = c; g = x; }
            else if (h < 120.0) { r = x; g = c; }
            else if (h < 180.0) { g = c; b = x; }
            else if (h < 240.0) { g = x; b = c; }
            else if (h < 300.0) { r = x; b = c; }
            else { r = c; b = x; }

            return Color.FromRgb(Ocho(r + m), Ocho(g + m), Ocho(b + m));
        }

        private static byte Ocho(double v)
        {
            int n = (int)Math.Round(v * 255.0);
            if (n < 0) { n = 0; }
            if (n > 255) { n = 255; }
            return (byte)n;
        }

        /* ---------------- ventana ---------------- */

        private void AlternarPantallaCompleta()
        {
            if (!pantallaCompleta)
            {
                estadoPrevio = WindowState;
                estiloPrevio = WindowStyle;
                modoPrevio = ResizeMode;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Normal;   // hace falta para que maximizar cubra la barra de tareas
                WindowState = WindowState.Maximized;
                pantallaCompleta = true;
            }
            else
            {
                WindowStyle = estiloPrevio;
                ResizeMode = modoPrevio;
                WindowState = estadoPrevio;
                pantallaCompleta = false;
            }
        }

        private const string XAML_BOTON =
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
              "<Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' " +
                      "BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='10'>" +
                "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' " +
                                  "Margin='{TemplateBinding Padding}'/>" +
              "</Border>" +
            "</ControlTemplate>";

        private static Button Boton(string texto)
        {
            Button b = new Button();
            b.Content = texto;
            b.FontSize = 14;
            b.Foreground = Pincel("#E8ECF4");
            b.Background = Pincel("#222736");
            b.BorderBrush = Pincel("#252A35");
            b.BorderThickness = new Thickness(1);
            b.Padding = new Thickness(16, 9, 16, 9);
            b.Margin = new Thickness(8, 0, 0, 0);
            b.Focusable = false;
            b.Template = (ControlTemplate)XamlReader.Parse(XAML_BOTON);
            return b;
        }

        private static Color Tono(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        private static Brush Pincel(string hex)
        {
            SolidColorBrush b = new SolidColorBrush(Tono(hex));
            b.Freeze();
            return b;
        }
    }

    /* ---------------- arranque ---------------- */

    public class Programa
    {
        [STAThread]
        public static void Main()
        {
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.Run(new Ventana());
        }
    }
}
