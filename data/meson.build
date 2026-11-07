desktop_file = configure_file(
  input: app_id + '.desktop.in',
  output: app_id + '.desktop',
  configuration: conf,
  install_dir: datadir / 'applications',
)

install_data(app_id + '.metainfo.xml', install_dir: datadir / 'metainfo')
install_data(app_id + '.svg', install_dir: datadir / 'icons/hicolor/scalable/apps')
install_data(app_id + '-symbolic.svg', install_dir: datadir / 'icons/hicolor/symbolic/apps')

if get_option('dolphin')
  configure_file(
    input: app_id + '.servicemenu.desktop.in',
    output: app_id + '.servicemenu.desktop',
    configuration: conf,
    install_dir: datadir / 'kio/servicemenus',
    install_mode: 'rwxr-xr-x',
  )
endif

desktop_validate = find_program('desktop-file-validate', required: false)
if desktop_validate.found()
  test('validate desktop file', desktop_validate, args: [desktop_file])
endif

appstreamcli = find_program('appstreamcli', required: false)
if appstreamcli.found()
  test('validate metainfo', appstreamcli,
    args: ['validate', '--no-net', files(app_id + '.metainfo.xml')])
endif
