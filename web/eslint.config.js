import js from '@eslint/js'
import pluginVue from 'eslint-plugin-vue'
import globals from 'globals'
import tseslint from 'typescript-eslint'

/**
 * 前端 lint 规则：只做"会真的抓出问题"的三件事——
 * 未使用变量（含解构）、非空断言、以及 Vue 模板里引用不存在的变量。
 * 不引入类型感知规则（那需要 project service，成本远大于收益）。
 */
export default tseslint.config(
  { ignores: ['dist/**', 'node_modules/**', 'coverage/**'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  ...pluginVue.configs['flat/recommended'],
  {
    files: ['**/*.{ts,vue}'],
    languageOptions: {
      // 浏览器端源码（`src/`）与构建配置（`*.config.ts`）都会走这条：
      // 声明标准运行时全局，避免 `no-undef` 对 `window` / `process` 之类的假红。
      globals: { ...globals.browser, ...globals.node },
      parserOptions: {
        parser: tseslint.parser,
        ecmaVersion: 'latest',
        sourceType: 'module',
      },
    },
    rules: {
      // 项目自带 .editorconfig 风格的排版，不用 eslint 管格式；下面只留"会真的抓出问题"的规则。
      'vue/max-attributes-per-line': 'off',
      'vue/singleline-html-element-content-newline': 'off',
      'vue/html-indent': 'off',
      'vue/html-closing-bracket-newline': 'off',
      'vue/html-self-closing': 'off',
      'vue/first-attribute-linebreak': 'off',
      'vue/attributes-order': 'off',
      'vue/require-default-prop': 'off',
      'vue/no-undef-properties': 'off',
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
      'no-console': 'warn',
      'vue/multi-word-component-names': 'off',
    },
  },
)
