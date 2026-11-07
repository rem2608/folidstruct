project('folidstruct',
  version: '0.1.0',
  license: 'GPL-3.0-or-later',
  meson_version: '>= 0.62.0',
)

app_id = 'io.github.rem2608.FolidStruct'
command = meson.project_name()

gnome = import('gnome')
python = import('python').find_installation('python3')

prefix = get_option('prefix')
bindir = get_option('bindir')
datadir = get_option('datadir')
pkgdatadir = datadir / command

conf = configuration_data()
conf.set('PYTHON', python.full_path())
conf.set('PKGDATADIR', prefix / pkgdatadir)
conf.set('COMMAND_PATH', prefix / bindir / command)

# Application
configure_file(
  input: 'bin' / command + '.in',
  output: command,
  configuration: conf,
  install_dir: bindir,
  install_mode: 'rwxr-xr-x',
)
install_subdir('folidstruct', install_dir: pkgdatadir, exclude_directories: ['__pycache__'])

subdir('data')

# File manager extensions: the same file, set up for each file manager.
foreach fm : [['nautilus', 'Nautilus'], ['nemo', 'Nemo'], ['caja', 'Caja']]
  if get_option(fm[0])
    fm_conf = configuration_data()
    fm_conf.merge_from(conf)
    fm_conf.set('FILE_MANAGER', fm[1])
    configure_file(
      input: 'extensions/folidstruct_menu.py',
      output: 'folidstruct_' + fm[0] + '.py',
      configuration: fm_conf,
      install_dir: datadir / fm[0] + '-python' / 'extensions',
    )
  endif
endforeach

test('unit tests', python,
  args: ['-m', 'unittest', 'discover', '-s', 'tests', '-t', '.'],
  workdir: meson.project_source_root(),
)

gnome.post_install(
  gtk_update_icon_cache: true,
  update_desktop_database: true,
)
