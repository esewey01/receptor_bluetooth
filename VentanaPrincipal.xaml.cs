using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace ReceptorBluetooth;

public partial class VentanaPrincipal : Window
{
    private readonly ObservableCollection<DeviceInformation> dispositivos = new();
    private DeviceWatcher? observador;
    private AudioPlaybackConnection? conexion;
    private string? dispositivoActivo;
    private bool ocupado;
    private bool cerrando;

    public VentanaPrincipal()
    {
        InitializeComponent();
        ListaDispositivos.ItemsSource = dispositivos;
    }

    private void AlCargar(object sender, RoutedEventArgs e)
    {
        try
        {
            observador = DeviceInformation.CreateWatcher(AudioPlaybackConnection.GetDeviceSelector());
            observador.Added += AlEncontrar;
            observador.Updated += AlActualizar;
            observador.Removed += AlQuitar;
            observador.EnumerationCompleted += AlTerminarBusqueda;
            observador.Stopped += AlDetenerBusqueda;
            observador.Start();
            Mostrar("Sin conexión", "Selecciona tu teléfono para recibir su audio.");
        }
        catch (Exception error)
        {
            Mostrar("No se pudo iniciar Bluetooth", error.Message);
            AvisoLista.Text = "Comprueba que el adaptador Bluetooth esté disponible y activado.";
        }
    }

    private void EnInterfaz(Action accion)
    {
        if (cerrando || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(() => { if (!cerrando) accion(); }));
    }

    private void AlEncontrar(DeviceWatcher sender, DeviceInformation equipo) => EnInterfaz(() =>
    {
        if (dispositivos.All(d => d.Id != equipo.Id)) dispositivos.Add(equipo);
        if (ListaDispositivos.SelectedItem is null) ListaDispositivos.SelectedItem = equipo;
        ActualizarLista();
    });

    private void AlActualizar(DeviceWatcher sender, DeviceInformationUpdate cambio) => EnInterfaz(() =>
    {
        dispositivos.FirstOrDefault(d => d.Id == cambio.Id)?.Update(cambio);
        ListaDispositivos.Items.Refresh();
    });

    private void AlQuitar(DeviceWatcher sender, DeviceInformationUpdate cambio) => EnInterfaz(() =>
    {
        var equipo = dispositivos.FirstOrDefault(d => d.Id == cambio.Id);
        if (equipo is not null) dispositivos.Remove(equipo);
        if (dispositivoActivo == cambio.Id)
        {
            LiberarConexion();
            Mostrar("Teléfono desconectado", "El dispositivo dejó de estar disponible. Vuelve a conectarlo e inténtalo de nuevo.");
        }
        ActualizarLista();
    });

    private void AlTerminarBusqueda(DeviceWatcher sender, object args) => EnInterfaz(ActualizarLista);

    private void AlDetenerBusqueda(DeviceWatcher sender, object args) => EnInterfaz(() =>
    {
        if (sender.Status == DeviceWatcherStatus.Aborted)
            Mostrar("Búsqueda interrumpida", "Cierra y vuelve a abrir la app después de comprobar Bluetooth en Windows.");
    });

    private void ActualizarLista()
    {
        AvisoLista.Text = dispositivos.Count == 0
            ? "No hay dispositivos disponibles. Activa Bluetooth y empareja tu teléfono en los ajustes de Windows."
            : "La lista se actualiza automáticamente al detectar dispositivos.";
        ActualizarBotones();
    }

    private async void AlConectar(object sender, RoutedEventArgs e)
    {
        if (ocupado || ListaDispositivos.SelectedItem is not DeviceInformation equipo) return;
        ocupado = true;
        LiberarConexion();
        ActualizarBotones();
        AudioPlaybackConnection? intento = null;
        try
        {
            Mostrar("Conectando…", $"Preparando el audio de {equipo.Name}.");
            intento = AudioPlaybackConnection.TryCreateFromId(equipo.Id);
            if (intento is null)
            {
                Mostrar("Dispositivo no disponible", "Comprueba que el teléfono esté encendido, emparejado y cerca de la laptop.");
                return;
            }
            conexion = intento;
            dispositivoActivo = equipo.Id;
            intento.StateChanged += AlCambiarEstado;
            await intento.StartAsync();
            // La ventana o el dispositivo pueden desaparecer durante la espera.
            if (cerrando || conexion != intento) return;
            var resultado = await intento.OpenAsync();
            if (cerrando || conexion != intento) return;
            if (resultado.Status == AudioPlaybackConnectionOpenResultStatus.Success)
            {
                Mostrar("Audio conectado", $"Reproduce música en {equipo.Name}. El sonido saldrá por la salida predeterminada de Windows.");
            }
            else
            {
                LiberarConexion();
                Mostrar("No se pudo conectar", $"Windows respondió: {resultado.Status}. Revisa Bluetooth y habilita «Audio multimedia» para la laptop en Android, si aparece esa opción.");
            }
        }
        catch (Exception error)
        {
            if (!cerrando && (intento is null || conexion == intento))
            {
                LiberarConexion();
                Mostrar("Error de conexión", $"Comprueba el teléfono y vuelve a intentarlo. {error.Message}");
            }
        }
        finally
        {
            ocupado = false;
            if (!cerrando) ActualizarBotones();
        }
    }

    private void AlCambiarEstado(AudioPlaybackConnection sender, object args) => EnInterfaz(() =>
    {
        if (conexion != sender) return;
        if (sender.State == AudioPlaybackConnectionState.Opened)
            Mostrar("Audio conectado", "Reproduce audio en tu teléfono. Ajusta el volumen en Android y en Windows.");
        else if (sender.State == AudioPlaybackConnectionState.Closed && !ocupado)
        {
            LiberarConexion();
            Mostrar("Sin conexión", "La conexión de audio terminó. Puedes volver a conectar el teléfono.");
        }
        ActualizarBotones();
    });

    private void AlDesconectar(object sender, RoutedEventArgs e)
    {
        LiberarConexion();
        Mostrar("Sin conexión", "La recepción de audio está desactivada.");
        ActualizarBotones();
    }

    private void LiberarConexion()
    {
        var anterior = conexion;
        conexion = null;
        dispositivoActivo = null;
        if (anterior is null) return;
        anterior.StateChanged -= AlCambiarEstado;
        anterior.Dispose();
    }

    private void AlSeleccionar(object sender, SelectionChangedEventArgs e) => ActualizarBotones();

    private void ActualizarBotones()
    {
        BotonConectar.IsEnabled = !ocupado && conexion is null && ListaDispositivos.SelectedItem is DeviceInformation;
        BotonDesconectar.IsEnabled = !ocupado && conexion is not null;
        ListaDispositivos.IsEnabled = !ocupado && conexion is null;
    }

    private void Mostrar(string estado, string detalle)
    {
        Estado.Text = estado;
        Detalle.Text = detalle;
    }

    private void AbrirBluetooth(object sender, RoutedEventArgs e) => AbrirAjustes("ms-settings:bluetooth");
    private void AbrirAudio(object sender, RoutedEventArgs e) => AbrirAjustes("ms-settings:sound");

    private void AbrirAjustes(string direccion)
    {
        try { Process.Start(new ProcessStartInfo(direccion) { UseShellExecute = true }); }
        catch (Exception error) { Mostrar("No se pudieron abrir los ajustes", error.Message); }
    }

    private void AlCerrar(object? sender, CancelEventArgs e)
    {
        cerrando = true;
        if (observador is not null)
        {
            observador.Added -= AlEncontrar;
            observador.Updated -= AlActualizar;
            observador.Removed -= AlQuitar;
            observador.EnumerationCompleted -= AlTerminarBusqueda;
            observador.Stopped -= AlDetenerBusqueda;
            if (observador.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                observador.Stop();
        }
        LiberarConexion();
    }
}
