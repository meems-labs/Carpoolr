import { spawnSync } from 'node:child_process'
import { mkdtemp, readFile, readdir, rm } from 'node:fs/promises'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import { basename, dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const webRoot = fileURLToPath(new URL('..', import.meta.url))
const outputPath = 'src/api/generated'
const checkOnly = process.argv.includes('--check')
const temporaryParent = resolve(tmpdir())
const temporaryPrefix = 'carpoolr-proto-check-'
const temporaryRoot = checkOnly ? await mkdtemp(join(temporaryParent, temporaryPrefix)) : undefined
const require = createRequire(import.meta.url)

try {
  // Invoke the npm-distributed Buf launcher through Bun, so no global Node/Buf is needed.
  const args = [require.resolve('@bufbuild/buf/bin/buf'), 'generate', '--template', 'buf.gen.yaml']
  if (temporaryRoot) args.push('--output', temporaryRoot)

  const generated = spawnSync(process.execPath, args, { cwd: webRoot, stdio: 'inherit' })
  if (generated.error) throw generated.error
  if (generated.status !== 0) process.exitCode = generated.status ?? 1
  else if (temporaryRoot) {
    const expected = await filesUnder(join(temporaryRoot, outputPath))
    const actual = await filesUnder(join(webRoot, outputPath))
    const names = new Set([...expected.keys(), ...actual.keys()])
    const changed = [...names].filter((name) => expected.get(name) !== actual.get(name))

    if (changed.length) {
      console.error('Generated protobuf files are out of date. Run bun run proto:generate:')
      for (const name of changed.sort()) console.error(`  ${join(outputPath, name)}`)
      process.exitCode = 1
    } else console.log('Generated protobuf files match the local API contract.')
  }
} finally {
  if (temporaryRoot) {
    const verifiedTarget = resolve(temporaryRoot)
    if (
      dirname(verifiedTarget) !== temporaryParent ||
      !basename(verifiedTarget).startsWith(temporaryPrefix)
    ) {
      throw new Error(
        'Refusing to remove a directory outside the generated-code check temporary path.',
      )
    }
    await rm(verifiedTarget, { recursive: true, force: true })
  }
}

async function filesUnder(root, current = root) {
  const files = new Map()
  let entries
  try {
    entries = await readdir(current, { withFileTypes: true })
  } catch (error) {
    if (error.code === 'ENOENT') return files
    throw error
  }

  for (const entry of entries) {
    const path = join(current, entry.name)
    if (entry.isDirectory()) {
      for (const [name, content] of await filesUnder(root, path)) files.set(name, content)
    } else if (entry.isFile()) {
      // Git's Windows autocrlf setting must not produce false codegen differences.
      files.set(relative(root, path), (await readFile(path, 'utf8')).replace(/\r\n/g, '\n'))
    }
  }
  return files
}
